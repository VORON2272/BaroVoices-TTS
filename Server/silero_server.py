import os
import io
import json
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
import torch
import soundfile as sf
from concurrent.futures import ThreadPoolExecutor
import sys
import os
import re
import hashlib
import torchaudio
import torchaudio.functional as F
import requests
import wave
import ssl

try:
    _create_unverified_https_context = ssl._create_unverified_context
except AttributeError:
    pass
else:
    ssl._create_default_https_context = _create_unverified_https_context

torch.set_grad_enabled(False)

try:
    torch.set_num_interop_threads(1)
except Exception:
    pass

CACHE_DIR = os.path.join(os.path.dirname(__file__), "tts_cache")
os.makedirs(CACHE_DIR, exist_ok=True)

lang_arg = sys.argv[1] if len(sys.argv) > 1 else 'en'
device_arg = sys.argv[2] if len(sys.argv) > 2 else 'cpu'
is_ru = (lang_arg == 'ru')

model_ru = None
model_en = None
device = None
executor = None
piper_models = {}
sample_rate = 24000

def initialize_server():
    global model_ru, model_en, device, executor, piper_models
    
    if device_arg != 'cpu' and torch.cuda.is_available():
        device = torch.device('cuda')
        print("Используется GPU (CUDA)" if is_ru else "Using GPU (CUDA)")
    else:
        device = torch.device('cpu')
        print("Используется CPU" if is_ru else "Using CPU")
    
    total_cores = os.cpu_count() or 4
    threads = max(2, total_cores // 2)
    torch.set_num_threads(threads)
    if is_ru:
        print(f"Оптимизация: выделено {threads} потоков из {total_cores} для нейросети (остальные свободны для игры).")
    else:
        print(f"Optimization: allocated {threads} out of {total_cores} CPU threads for TTS.")
    
    executor = ThreadPoolExecutor(max_workers=1)
    
    print("Загрузка нейросети Silero TTS... Это может занять несколько минут при первом запуске." if is_ru else "Loading Silero TTS neural network... This may take a few minutes on first run.")
    try:
        print("Загрузка русской модели v4_ru..." if is_ru else "Loading Russian model v4_ru...")
        model_ru, _ = torch.hub.load(repo_or_dir='snakers4/silero-models',
                                             model='silero_tts',
                                             language='ru',
                                             speaker='v4_ru',
                                             trust_repo=True)
        model_ru.to(device)
        
        print("Загрузка английской модели v3_en..." if is_ru else "Loading English model v3_en...")
        model_en, _ = torch.hub.load(repo_or_dir='snakers4/silero-models',
                                             model='silero_tts',
                                             language='en',
                                             speaker='v3_en',
                                             trust_repo=True)
        model_en.to(device)
        
        print("Модели успешно загружены!" if is_ru else "Models loaded successfully!")
        print("Прогрев нейросетей (это уберет лаг при первой фразе)..." if is_ru else "Warming up neural networks...")
        
        try:
            model_ru.apply_tts(text="Проверка связи", speaker="baya", sample_rate=sample_rate)
            model_en.apply_tts(text="Testing connection", speaker="en_0", sample_rate=sample_rate)
            print("Прогрев полностью завершен! Сервер готов к мгновенной работе." if is_ru else "Warmup fully complete! Server ready.")
        except Exception:
            pass
    except Exception as e:
        print(f"Ошибка загрузки моделей: {e}" if is_ru else f"Error loading models: {e}")

class RequestHandler(BaseHTTPRequestHandler):
    def do_GET(self):
        self.send_response(200)
        self.send_header('Content-Type', 'application/json')
        self.end_headers()
        self.wfile.write(json.dumps({"status": "ok"}).encode('utf-8'))

    def _generate_audio(self, text, speaker, req_sample_rate, boost, msg_type, distance, rate=0, engine='silero'):
        has_cyrillic = bool(re.search('[а-яА-ЯёЁ]', text))
        has_chinese = bool(re.search(r'[\u4e00-\u9fff]', text))
        
        if has_chinese and engine == "piper":
            active_model = model_en
            active_speaker = "zh_huayan" if not speaker.startswith("zh_") else speaker
            lang_label = "ZH"
        elif has_cyrillic:
            active_model = model_ru
            if speaker.startswith('en_'):
                if speaker in ['en_13', 'en_15', 'en_22']:
                    active_speaker = "aidar" # Map English males to Russian male
                else:
                    active_speaker = "baya"  # Map English females to Russian female
            else:
                active_speaker = speaker
            lang_label = "RU"
        else:
            active_model = model_en
            if speaker.startswith('en_'):
                active_speaker = speaker
            else:
                en_speaker_map = {
                    "baya": "en_0",     # Female
                    "kseniya": "en_4",  # Female
                    "xenia": "en_5",    # Female
                    "eugene": "en_15",  # Male
                    "aidar": "en_13"    # Male
                }
                active_speaker = en_speaker_map.get(speaker, "en_13")
            lang_label = "EN"
            
        print(f"[{lang_label}] Генерируем голос ({msg_type}) [Движок: {engine}]: {active_speaker} -> {text} (Boost: {boost}x)" if is_ru else f"[{lang_label}] Generating voice ({msg_type}) [Engine: {engine}]: {active_speaker} -> {text} (Boost: {boost}x)")
        
        cache_key_raw = f"{text}|{active_speaker}|{req_sample_rate}|{engine}|v5"
        cache_key = hashlib.md5(cache_key_raw.encode('utf-8')).hexdigest()
        cache_path = os.path.join(CACHE_DIR, f"{cache_key}.pt")
        
        if os.path.exists(cache_path):
            audio = torch.load(cache_path, weights_only=True)
            try:
                os.utime(cache_path, None)
            except:
                pass
        else:
            if engine == "piper":
                try:
                    import urllib.request
                    from piper import PiperVoice
                    
                    piper_dir = os.path.join(os.path.dirname(__file__), "piper_models")
                    if not os.path.exists(piper_dir):
                        os.makedirs(piper_dir)
                        
                    speaker_id = None
                    if active_speaker.startswith("zh_"):
                        short_name = active_speaker.split('_', 1)[1]
                        model_name = f"zh_CN-{short_name}-medium"
                        json_url = f"https://huggingface.co/rhasspy/piper-voices/resolve/main/zh/zh_CN/{short_name}/medium/{model_name}.onnx.json"
                        onnx_url = f"https://huggingface.co/rhasspy/piper-voices/resolve/main/zh/zh_CN/{short_name}/medium/{model_name}.onnx"
                    elif has_cyrillic:
                        if active_speaker in ["aidar"]:
                            model_name = "ru_RU-dmitri-medium"
                        elif active_speaker in ["ru_denis", "eugene"]:
                            model_name = "ru_RU-denis-medium"
                        elif active_speaker == "ru_ruslan":
                            model_name = "ru_RU-ruslan-medium"
                        else:
                            model_name = "ru_RU-irina-medium"
                            
                        short_name = model_name.split('-')[1]
                        json_url = f"https://huggingface.co/rhasspy/piper-voices/resolve/v1.0.0/ru/ru_RU/{short_name}/medium/{model_name}.onnx.json"
                        onnx_url = f"https://huggingface.co/rhasspy/piper-voices/resolve/v1.0.0/ru/ru_RU/{short_name}/medium/{model_name}.onnx"
                    else:
                        model_name = "en_US-arctic-medium"
                        json_url = "https://huggingface.co/rhasspy/piper-voices/resolve/v1.0.0/en/en_US/arctic/medium/en_US-arctic-medium.onnx.json"
                        onnx_url = "https://huggingface.co/rhasspy/piper-voices/resolve/v1.0.0/en/en_US/arctic/medium/en_US-arctic-medium.onnx"
                        arctic_map = {"en_0": 0, "en_1": 1, "en_2": 3, "en_3": 7, "en_4": 2, "en_5": 4, "en_6": 7, "en_7": 14}
                        speaker_id = arctic_map.get(active_speaker, 0)
                        
                    onnx_path = os.path.join(piper_dir, f"{model_name}.onnx")
                    json_path = os.path.join(piper_dir, f"{model_name}.onnx.json")
                    
                    if not os.path.exists(onnx_path) or not os.path.exists(json_path):
                        print(f"Скачивание модели Piper {model_name} (это займет время)..." if is_ru else f"Downloading Piper model {model_name}...")
                        import warnings
                        from urllib3.exceptions import InsecureRequestWarning
                        warnings.simplefilter('ignore', InsecureRequestWarning)
                        open(onnx_path, 'wb').write(requests.get(onnx_url, verify=False).content)
                        open(json_path, 'wb').write(requests.get(json_url, verify=False).content)
                        print("Скачивание завершено!" if is_ru else "Download complete!")
                        
                    global piper_models
                    if model_name not in piper_models:
                        piper_models[model_name] = PiperVoice.load(onnx_path, config_path=json_path)
                    
                    voice = piper_models[model_name]
                    
                    wav_io = io.BytesIO()
                    with wave.open(wav_io, 'wb') as wav_file:
                        if speaker_id is not None:
                            from piper.config import SynthesisConfig
                            syn_config = SynthesisConfig(speaker_id=speaker_id)
                            voice.synthesize_wav(text, wav_file, syn_config=syn_config)
                        else:
                            voice.synthesize_wav(text, wav_file)
                    
                    wav_io.seek(0)
                    audio_np, orig_sr = sf.read(wav_io)
                    audio = torch.from_numpy(audio_np).float()
                    
                    if orig_sr != req_sample_rate:
                        audio = F.resample(audio, orig_sr, req_sample_rate)
                        
                    audio = audio.squeeze(0)
                    
                except Exception as e:
                    print(f"Ошибка генерации Piper: {e}. Переключение на Silero." if is_ru else f"Piper generation error: {e}. Falling back to Silero.")
                    audio = active_model.apply_tts(text=text, speaker=active_speaker, sample_rate=req_sample_rate)
            else:
                audio = active_model.apply_tts(text=text, speaker=active_speaker, sample_rate=req_sample_rate)
                
            # Trim trailing silence/sighs from Piper/Silero
            threshold = 0.005
            window_size = int(req_sample_rate * 0.02)
            end_idx = len(audio)
            for i in range(len(audio) - window_size, 0, -window_size):
                if audio[i:i+window_size].abs().mean() > threshold:
                    end_idx = min(len(audio), i + window_size + int(req_sample_rate * 0.05))
                    break
            audio = audio[:end_idx]
                
            fade_samples = int(req_sample_rate * 0.05) # 50 ms fade
            if len(audio) > fade_samples * 2:
                fade = torch.linspace(1.0, 0.0, fade_samples, device=audio.device)
                audio[-fade_samples:] *= fade
                audio[:fade_samples] *= fade.flip(0)
                
            torch.save(audio, cache_path)
            
            try:
                cache_files = [os.path.join(CACHE_DIR, f) for f in os.listdir(CACHE_DIR) if f.endswith('.pt')]
                if len(cache_files) > 500:
                    cache_files.sort(key=os.path.getmtime)
                    for f in cache_files[:-500]:
                        try:
                            os.remove(f)
                        except:
                            pass
            except:
                pass
        
        if boost != 1.0:
            audio = audio * boost
            audio = torch.clamp(audio, -1.0, 1.0)
            
        if msg_type == "Radio":
            base_noise = 0.08
            dist_noise = min(max(distance - 1000.0, 0.0) / 4000.0, 1.0) * 0.25
            noise_level = base_noise + dist_noise
            
            noise = torch.randn_like(audio) * noise_level
            audio = audio + noise
                
            audio = torch.clamp(audio, -1.0, 1.0)

            try:
                audio_2d = audio.unsqueeze(0)
                audio_2d = F.highpass_biquad(audio_2d, req_sample_rate, 600.0)
                audio_2d = F.lowpass_biquad(audio_2d, req_sample_rate, 2500.0)
                audio_2d = audio_2d * 2.5
                audio = audio_2d.squeeze(0)
            except Exception as e:
                print(f"Ошибка аудиофильтра рации: {e}" if is_ru else f"Radio filter error: {e}")
        playback_rate = int(req_sample_rate * (1.0 + (rate * 0.03)))
        
        silence_pad = torch.zeros(int(req_sample_rate * 0.3))
        audio = torch.cat([audio, silence_pad])
        
        buffer = io.BytesIO()
        sf.write(buffer, audio.numpy(), playback_rate, format='OGG', subtype='VORBIS')
        buffer.seek(0)
        return buffer

    def do_POST(self):
        content_length = int(self.headers['Content-Length'])
        post_data = self.rfile.read(content_length)
        data = json.loads(post_data.decode('utf-8'))
        
        text = data.get('text', 'Привет')
        speaker = data.get('voice', 'baya')
        boost = float(data.get('boost', 100)) / 100.0
        msg_type = data.get('msg_type', 'Default')
        distance = float(data.get('distance', 0.0))
        rate = int(data.get('rate', 0))
        engine = data.get('engine', 'silero')
        
        req_sample_rate = int(data.get('sample_rate', 24000))
        if req_sample_rate not in [8000, 24000, 48000]:
            req_sample_rate = 24000
        
        if not text:
            self.send_response(400)
            self.end_headers()
            return
            
        text = text.strip()
        if text[-1] not in ['.', '!', '?', ',', ':', ';', '…', '"', "'"]:
            text += '.'
            
        try:
            future = executor.submit(self._generate_audio, text, speaker, req_sample_rate, boost, msg_type, distance, rate, engine)
            buffer = future.result()
            
            self.send_response(200)
            self.send_header('Content-type', 'audio/ogg')
            self.end_headers()
            self.wfile.write(buffer.read())
        except Exception as e:
            print(f"Ошибка генерации: {e}" if is_ru else f"Generation error: {e}")
            self.send_response(500)
            self.end_headers()
            self.wfile.write(str(e).encode('utf-8'))

def run(server_class=ThreadingHTTPServer, handler_class=RequestHandler, port=5000):
    server_address = ('127.0.0.1', port)
    httpd = server_class(server_address, handler_class)
    print("Сервер запущен. Ожидание запросов..." if is_ru else f"Server started. Waiting for requests on port {port}...")
    httpd.serve_forever()

if __name__ == '__main__':
    initialize_server()
    run()
