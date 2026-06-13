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

model_id = 'v4_ru'
language = 'ru'
sample_rate = 24000

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
    
    def do_warmup():
        try:
            model_ru.apply_tts(text="Проверка связи", speaker="baya", sample_rate=sample_rate)
            model_en.apply_tts(text="Testing connection", speaker="en_0", sample_rate=sample_rate)
            print("Прогрев полностью завершен! Сервер готов к мгновенной работе." if is_ru else "Warmup fully complete! Server ready.")
        except Exception:
            pass

    do_warmup()

except Exception as e:
    print(f"Ошибка загрузки моделей: {e}" if is_ru else f"Error loading models: {e}")

class RequestHandler(BaseHTTPRequestHandler):
    def do_GET(self):
        self.send_response(200)
        self.send_header('Content-Type', 'application/json')
        self.end_headers()
        self.wfile.write(json.dumps({"status": "ok"}).encode('utf-8'))

    def _generate_audio(self, text, speaker, req_sample_rate, boost, msg_type, distance, rate=0):
        has_cyrillic = bool(re.search('[а-яА-ЯёЁ]', text))
        
        if has_cyrillic:
            active_model = model_ru
            if speaker.startswith('en_'):
                active_speaker = "baya"
            else:
                active_speaker = speaker
            lang_label = "RU"
        else:
            active_model = model_en
            en_speaker_map = {
                "baya": "en_1",
                "kseniya": "en_3",
                "xenia": "en_5",
                "eugene": "en_2",
                "aidar": "en_0"
            }
            active_speaker = en_speaker_map.get(speaker, "en_0")
            lang_label = "EN"
            
        print(f"[{lang_label}] Генерируем голос ({msg_type}): {active_speaker} -> {text} (Boost: {boost}x)" if is_ru else f"[{lang_label}] Generating voice ({msg_type}): {active_speaker} -> {text} (Boost: {boost}x)")
        
        cache_key_raw = f"{text}|{active_speaker}|{req_sample_rate}"
        cache_key = hashlib.md5(cache_key_raw.encode('utf-8')).hexdigest()
        cache_path = os.path.join(CACHE_DIR, f"{cache_key}.pt")
        
        if os.path.exists(cache_path):
            audio = torch.load(cache_path, weights_only=True)
            try:
                os.utime(cache_path, None)
            except:
                pass
        else:
            audio = active_model.apply_tts(text=text,
                                           speaker=active_speaker,
                                           sample_rate=req_sample_rate)
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
                import torchaudio.functional as F
                audio_2d = audio.unsqueeze(0)
                audio_2d = F.highpass_biquad(audio_2d, req_sample_rate, 600.0)
                audio_2d = F.lowpass_biquad(audio_2d, req_sample_rate, 2500.0)
                audio_2d = audio_2d * 2.5
                audio = audio_2d.squeeze(0)
            except Exception as e:
                print(f"Ошибка аудиофильтра рации: {e}" if is_ru else f"Radio filter error: {e}")
        playback_rate = int(req_sample_rate * (1.0 + (rate * 0.03)))
        
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
        
        req_sample_rate = int(data.get('sample_rate', 24000))
        if req_sample_rate not in [8000, 24000, 48000]:
            req_sample_rate = 24000
        
        if not text.strip():
            self.send_response(400)
            self.end_headers()
            return
            
        try:
            future = executor.submit(self._generate_audio, text, speaker, req_sample_rate, boost, msg_type, distance, rate)
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
    run()
