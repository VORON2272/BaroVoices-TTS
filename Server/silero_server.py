import os
import io
import json
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
import torch
import soundfile as sf
from concurrent.futures import ThreadPoolExecutor
import sys
import re
import hashlib
import torchaudio
import torchaudio.functional as F
import requests
import wave
import ssl

try:
    if hasattr(sys.stdout, 'reconfigure'):
        sys.stdout.reconfigure(encoding='utf-8', errors='replace')
    if hasattr(sys.stderr, 'reconfigure'):
        sys.stderr.reconfigure(encoding='utf-8', errors='replace')
except Exception:
    pass

try:
    import piper_phonemize
    espeak_path = os.path.join(os.path.dirname(piper_phonemize.__file__), 'espeak-ng-data')
    if os.path.exists(espeak_path):
        os.environ['PIPER_ESPEAKNG_DATA_DIRECTORY'] = espeak_path
except Exception:
    pass

FastChinesePhonemizer = None
try:
    import pypinyin
    from piper.phonemize_chinese import PHONEME_TO_ID, _normalize_g2pw_syllable, _split_initial_final_tone

    class FastChinesePhonemizer:
        def phonemize(self, text: str):
            pinyin_list = pypinyin.pinyin(text, style=pypinyin.Style.TONE3, neutral_tone_with_five=True)
            sentence_phonemes = []
            for item in pinyin_list:
                raw = item[0]
                if raw in PHONEME_TO_ID:
                    sentence_phonemes.append(raw)
                    continue
                syl = _normalize_g2pw_syllable(raw)
                ini_p, fin_p, tone = _split_initial_final_tone(syl)
                if not fin_p:
                    if syl in PHONEME_TO_ID:
                        sentence_phonemes.append(syl)
                    continue
                if not ini_p:
                    ini_p = 'Ø'
                for sym in (ini_p, fin_p, tone):
                    if sym and sym in PHONEME_TO_ID:
                        sentence_phonemes.append(sym)
            return [sentence_phonemes]
except Exception:
    pass

try:
    from piper import PhonemeType
except Exception:
    PhonemeType = None

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
try:
    import piper
    from piper import PiperVoice
    piper_available = True
except Exception:
    piper_available = False

def ensure_piper_model(model_name, piper_dir):
    onnx_path = os.path.join(piper_dir, f"{model_name}.onnx")
    json_path = os.path.join(piper_dir, f"{model_name}.onnx.json")
    
    if os.path.exists(onnx_path) and os.path.getsize(onnx_path) > 1000000 and \
       os.path.exists(json_path) and os.path.getsize(json_path) > 100:
        return onnx_path, json_path

    # Try fast GitHub release mirror (sherpa-onnx prebuilt archives)
    gh_url = f"https://github.com/k2-fsa/sherpa-onnx/releases/download/tts-models/vits-piper-{model_name}.tar.bz2"
    try:
        print(f"Загрузка модели {model_name} (GitHub зеркало)..." if is_ru else f"Downloading model {model_name} (GitHub mirror)...")
        r = requests.get(gh_url, stream=True, timeout=60)
        if r.status_code == 200:
            import tarfile, shutil
            tmp_tar = os.path.join(piper_dir, f"{model_name}.tar.bz2.tmp")
            with open(tmp_tar, 'wb') as f:
                for chunk in r.iter_content(chunk_size=1024 * 1024):
                    if chunk:
                        f.write(chunk)
            with tarfile.open(tmp_tar, 'r:bz2') as tar:
                for m in tar.getmembers():
                    if m.name.endswith(f"{model_name}.onnx"):
                        with tar.extractfile(m) as f_in, open(onnx_path, 'wb') as f_out:
                            shutil.copyfileobj(f_in, f_out)
                    elif m.name.endswith(f"{model_name}.onnx.json"):
                        with tar.extractfile(m) as f_in, open(json_path, 'wb') as f_out:
                            shutil.copyfileobj(f_in, f_out)
            if os.path.exists(tmp_tar):
                os.remove(tmp_tar)
            if os.path.exists(onnx_path) and os.path.getsize(onnx_path) > 1000000:
                print(f"Модель {model_name} успешно загружена и готова!" if is_ru else f"Model {model_name} ready!")
                return onnx_path, json_path
    except Exception as e:
        pass
    
    return onnx_path, json_path

def download_piper_file(url, target_path, expected_min_size=1000):
    if os.path.exists(target_path) and os.path.getsize(target_path) >= expected_min_size:
        return
    tmp_path = target_path + ".tmp"
    try:
        if os.path.exists(tmp_path):
            os.remove(tmp_path)
    except Exception:
        pass
    
    file_name = os.path.basename(target_path)
    
    urls_to_try = [url]
    if "huggingface.co" in url:
        mirror_url = url.replace("https://huggingface.co/", "https://hf-mirror.com/")
        urls_to_try.append(mirror_url)
    
    last_err = None
    for attempt_url in urls_to_try:
        try:
            domain = "hf-mirror" if "hf-mirror" in attempt_url else "huggingface"
            print(f"Загрузка файла {file_name}..." if is_ru else f"Downloading {file_name}...")
            resp = requests.get(attempt_url, stream=True, timeout=30, allow_redirects=True)
            resp.raise_for_status()
            
            with open(tmp_path, 'wb') as f:
                for chunk in resp.iter_content(chunk_size=1024 * 64):
                    if chunk:
                        f.write(chunk)
            
            if os.path.exists(tmp_path) and os.path.getsize(tmp_path) >= expected_min_size:
                if os.path.exists(target_path):
                    os.remove(target_path)
                os.rename(tmp_path, target_path)
                print(f"Файл {file_name} успешно сохранён!" if is_ru else f"{file_name} saved successfully!")
                return
            else:
                sz = os.path.getsize(tmp_path) if os.path.exists(tmp_path) else 0
                raise RuntimeError(f"Файл {file_name} слишком мал ({sz} байт)")
        except Exception as e:
            last_err = e
            try:
                if os.path.exists(tmp_path):
                    os.remove(tmp_path)
            except Exception:
                pass
    
    raise RuntimeError(f"Не удалось скачать {file_name}: {last_err}")

def initialize_server():
    global model_ru, model_en, device, executor, piper_models, piper_available
    
    if device_arg in ['gpu', 'cuda'] and torch.cuda.is_available():
        device = torch.device('cuda')
        print("Используется GPU (CUDA)" if is_ru else "Using GPU (CUDA)")
    elif device_arg in ['mps', 'gpu'] and hasattr(torch.backends, 'mps') and torch.backends.mps.is_available():
        device = torch.device('mps')
        print("Используется Apple Silicon GPU (MPS / Metal)" if is_ru else "Using Apple Silicon GPU (MPS / Metal)")
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
    
    executor = ThreadPoolExecutor(max_workers=2)
    
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
        
        print("Модели Silero успешно загружены!" if is_ru else "Silero models loaded successfully!")
        print("Прогрев нейросетей (это уберет лаг при первой фразе)..." if is_ru else "Warming up neural networks...")
        
        try:
            model_ru.apply_tts(text="Проверка связи", speaker="baya", sample_rate=sample_rate)
            model_en.apply_tts(text="Testing connection", speaker="en_0", sample_rate=sample_rate)
            print("Прогрев полностью завершен! Сервер готов к мгновенной работе." if is_ru else "Warmup fully complete! Server ready.")
        except Exception:
            pass
    except Exception as e:
        print(f"Ошибка загрузки моделей Silero: {e}" if is_ru else f"Error loading Silero models: {e}")

    try:
        import piper
        from piper import PiperVoice
        piper_available = True
        print("Движок Piper TTS готов к параллельной работе." if is_ru else "Piper TTS engine ready for parallel operation.")
    except Exception as pe:
        piper_available = False
        print(f"Внимание: piper-tts не загружен ({pe}). Будет инициализирован по требованию." if is_ru else f"Warning: piper-tts not loaded ({pe}). Will init on demand.")

class RequestHandler(BaseHTTPRequestHandler):
    def do_GET(self):
        self.send_response(200)
        self.send_header('Content-Type', 'application/json')
        self.end_headers()
        self.wfile.write(json.dumps({"status": "ok"}).encode('utf-8'))

    def _generate_audio(self, text, speaker, req_sample_rate, boost, msg_type, distance, rate=0, engine='silero', radio_filter=True):
        inference_ctx = torch.inference_mode if hasattr(torch, 'inference_mode') else torch.no_grad
        with inference_ctx():
            return self._generate_audio_impl(text, speaker, req_sample_rate, boost, msg_type, distance, rate, engine, radio_filter)

    def _generate_audio_impl(self, text, speaker, req_sample_rate, boost, msg_type, distance, rate=0, engine='silero', radio_filter=True):
        has_cyrillic = bool(re.search('[а-яА-ЯёЁ]', text))
        has_chinese = bool(re.search(r'[\u4e00-\u9fff]', text))
        
        if (has_chinese or speaker.startswith("zh_")) and engine == "piper":
            active_model = model_en
            active_speaker = speaker if speaker.startswith("zh_") else "zh_huayan"
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
            
        try:
            print(f"[{lang_label}] Генерируем голос ({msg_type}) [Движок: {engine}]: {active_speaker} -> {text} (Boost: {boost}x)" if is_ru else f"[{lang_label}] Generating voice ({msg_type}) [Engine: {engine}]: {active_speaker} -> {text} (Boost: {boost}x)")
        except Exception:
            try:
                safe_text = text.encode('ascii', errors='backslashreplace').decode('ascii')
                print(f"[{lang_label}] Voice ({msg_type}) [{engine}]: {active_speaker} -> {safe_text}")
            except Exception:
                pass
        
        cache_key_raw = f"{text}|{active_speaker}|{req_sample_rate}|{engine}|v5"
        cache_key = hashlib.md5(cache_key_raw.encode('utf-8')).hexdigest()
        cache_path = os.path.join(CACHE_DIR, f"{cache_key}.pt")
        
        fallback_triggered = False
        fallback_reason = ""
        actual_engine = engine

        if os.path.exists(cache_path):
            audio = torch.load(cache_path, weights_only=True)
            try:
                os.utime(cache_path, None)
            except:
                pass
        else:
            if engine == "piper":
                try:
                    global piper_available
                    if not piper_available:
                        try:
                            import subprocess
                            print("Библиотека piper-tts не найдена. Автоустановка..." if is_ru else "Library piper-tts not found. Auto-installing...")
                            subprocess.check_call([sys.executable, "-m", "pip", "install", "piper-tts"])
                            import piper
                            from piper import PiperVoice
                            piper_available = True
                            print("piper-tts успешно установлен!" if is_ru else "piper-tts installed successfully!")
                        except Exception as ie:
                            raise RuntimeError(f"Не удалось установить piper-tts: {ie}")
                    else:
                        from piper import PiperVoice
                    
                    piper_dir = os.path.join(os.path.dirname(__file__), "piper_models")
                    if not os.path.exists(piper_dir):
                        os.makedirs(piper_dir, exist_ok=True)
                        
                    speaker_id = None
                    pitch_shift_steps = 0.0
                    length_scale = 1.0

                    zh_speakers = {
                        "zh_huayan": ("zh_CN-huayan-medium", "huayan", 0.0, 1.0),
                        "zh_huayan_officer": ("zh_CN-huayan-medium", "huayan", -2.0, 0.95),
                        "zh_huayan_cadet": ("zh_CN-huayan-medium", "huayan", 1.8, 1.05),
                        "zh_xiao_ya": ("zh_CN-xiao_ya-medium", "xiao_ya", 0.0, 1.0),
                        "zh_xiao_ya_medic": ("zh_CN-xiao_ya-medium", "xiao_ya", 1.2, 1.02),
                        "zh_chaowen": ("zh_CN-chaowen-medium", "chaowen", 0.0, 1.0),
                        "zh_chaowen_captain": ("zh_CN-chaowen-medium", "chaowen", -1.5, 0.94),
                        "zh_chaowen_engineer": ("zh_CN-chaowen-medium", "chaowen", 0.8, 1.0),
                    }

                    if active_speaker in zh_speakers:
                        model_name, short_name, pitch_shift_steps, length_scale = zh_speakers[active_speaker]
                        json_url = f"https://huggingface.co/rhasspy/piper-voices/resolve/main/zh/zh_CN/{short_name}/medium/{model_name}.onnx.json"
                        onnx_url = f"https://huggingface.co/rhasspy/piper-voices/resolve/main/zh/zh_CN/{short_name}/medium/{model_name}.onnx"
                    elif active_speaker.startswith("zh_"):
                        short_name = active_speaker.split('_', 1)[1]
                        model_name = f"zh_CN-{short_name}-medium"
                        json_url = f"https://huggingface.co/rhasspy/piper-voices/resolve/main/zh/zh_CN/{short_name}/medium/{model_name}.onnx.json"
                        onnx_url = f"https://huggingface.co/rhasspy/piper-voices/resolve/main/zh/zh_CN/{short_name}/medium/{model_name}.onnx"
                    elif has_cyrillic:
                        ru_speakers = {
                            "aidar": (0, "ru_RU-dmitri-medium", "dmitri"),
                            "baya": (0, "ru_RU-irina-medium", "irina"),
                            "kseniya": (0, "ru_RU-denis-medium", "denis"),
                            "xenia": (0, "ru_RU-ruslan-medium", "ruslan"),
                            "eugene": (0, "ru_RU-dmitri-medium", "dmitri")
                        }
                        sp_info = ru_speakers.get(active_speaker, (0, "ru_RU-irina-medium", "irina"))
                        speaker_id = sp_info[0]
                        model_name = sp_info[1]
                        short_name = sp_info[2]
                        json_url = f"https://huggingface.co/rhasspy/piper-voices/resolve/v1.0.0/ru/ru_RU/{short_name}/medium/{model_name}.onnx.json"
                        onnx_url = f"https://huggingface.co/rhasspy/piper-voices/resolve/v1.0.0/ru/ru_RU/{short_name}/medium/{model_name}.onnx"
                    else:
                        model_name = "en_US-arctic-medium"
                        json_url = "https://huggingface.co/rhasspy/piper-voices/resolve/v1.0.0/en/en_US/arctic/medium/en_US-arctic-medium.onnx.json"
                        onnx_url = "https://huggingface.co/rhasspy/piper-voices/resolve/v1.0.0/en/en_US/arctic/medium/en_US-arctic-medium.onnx"
                        if active_speaker.startswith("en_"):
                            try:
                                speaker_id = int(active_speaker.split("_")[1])
                            except Exception:
                                speaker_id = 0
                                
                    onnx_path, json_path = ensure_piper_model(model_name, piper_dir)
                    if not (os.path.exists(onnx_path) and os.path.getsize(onnx_path) > 1000000 and os.path.exists(json_path) and os.path.getsize(json_path) > 100):
                        download_piper_file(json_url, json_path, expected_min_size=100)
                        download_piper_file(onnx_url, onnx_path, expected_min_size=1000000)
                        
                    global piper_models
                    if model_name not in piper_models:
                        v = PiperVoice.load(onnx_path, config_path=json_path)
                        if hasattr(v, "config") and getattr(v.config, "phoneme_type", None) == PhonemeType.PINYIN and FastChinesePhonemizer is not None:
                            v._chinese_phonemizer = FastChinesePhonemizer()
                        piper_models[model_name] = v
                    
                    voice = piper_models[model_name]
                    
                    wav_io = io.BytesIO()
                    with wave.open(wav_io, 'wb') as wav_file:
                        from piper.config import SynthesisConfig
                        syn_kwargs = {}
                        if speaker_id is not None:
                            syn_kwargs["speaker_id"] = speaker_id
                        if length_scale != 1.0:
                            syn_kwargs["length_scale"] = length_scale
                            
                        if syn_kwargs:
                            syn_config = SynthesisConfig(**syn_kwargs)
                            voice.synthesize_wav(text, wav_file, syn_config=syn_config)
                        else:
                            voice.synthesize_wav(text, wav_file)
                    
                    wav_io.seek(0)
                    audio_np, orig_sr = sf.read(wav_io)
                    audio = torch.from_numpy(audio_np).float()
                    
                    if pitch_shift_steps != 0.0:
                        audio = F.pitch_shift(audio.unsqueeze(0), orig_sr, n_steps=pitch_shift_steps).squeeze(0)
                        
                    if orig_sr != req_sample_rate:
                        audio = F.resample(audio, orig_sr, req_sample_rate)
                        
                    audio = audio.squeeze(0) if audio.dim() > 1 else audio
                    actual_engine = "piper"
                    
                except Exception as e:
                    print(f"Ошибка генерации Piper: {e}. Переключение на Silero." if is_ru else f"Piper generation error: {e}. Falling back to Silero.")
                    fallback_triggered = True
                    fallback_reason = str(e)
                    actual_engine = "silero"
                    fb_spk = "aidar" if has_cyrillic else "en_0"
                    if active_speaker in ["baya", "aidar", "kseniya", "xenia", "eugene", "en_0", "en_13", "en_15", "en_22"]:
                        fb_spk = active_speaker
                    audio = active_model.apply_tts(text=text, speaker=fb_spk, sample_rate=req_sample_rate)
            else:
                actual_engine = "silero"
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
                
            audio_to_save = audio.detach().cpu()
            torch.save(audio_to_save, cache_path)
            
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
            
        if msg_type == "Radio" and radio_filter:
            try:
                audio_2d = audio.unsqueeze(0)
                # 350Hz highpass cuts rumble, 5000Hz lowpass ensures crisp speech & clear consonants
                audio_2d = F.highpass_biquad(audio_2d, req_sample_rate, 350.0)
                audio_2d = F.lowpass_biquad(audio_2d, req_sample_rate, 5000.0)
                
                # Soft analog saturation for authentic walkie-talkie speaker warmth
                audio_2d = torch.tanh(audio_2d * 1.25)
                audio = audio_2d.squeeze(0)
            except Exception as e:
                print(f"Ошибка аудиофильтра рации: {e}" if is_ru else f"Radio filter error: {e}")

            # Subtle tactical radio carrier static
            base_noise = 0.015
            dist_noise = min(max(distance - 1500.0, 0.0) / 4000.0, 1.0) * 0.04
            noise_level = base_noise + dist_noise
            
            noise = torch.randn_like(audio) * noise_level
            audio = audio + noise
            audio = torch.clamp(audio, -1.0, 1.0)
        playback_rate = int(req_sample_rate * (1.0 + (rate * 0.03)))
        
        silence_pad = torch.zeros(int(req_sample_rate * 0.1), device=audio.device)
        audio = torch.cat([audio, silence_pad])
        
        buffer = io.BytesIO()
        audio_np = audio.detach().cpu().numpy()
        sf.write(buffer, audio_np, playback_rate, format='OGG', subtype='VORBIS')
        buffer.seek(0)
        return buffer, fallback_triggered, fallback_reason, actual_engine

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
        radio_filter = bool(data.get('radio_filter', True))
        
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
            future = executor.submit(self._generate_audio, text, speaker, req_sample_rate, boost, msg_type, distance, rate, engine, radio_filter)
            buffer, fallback_triggered, fallback_reason, actual_engine = future.result()
            
            self.send_response(200)
            self.send_header('Content-type', 'audio/ogg')
            self.send_header('X-TTS-Engine', str(actual_engine))
            self.send_header('X-TTS-Fallback', 'true' if fallback_triggered else 'false')
            if fallback_triggered and fallback_reason:
                clean_reason = re.sub(r'[\r\n]+', ' ', str(fallback_reason))[:120]
                self.send_header('X-TTS-Fallback-Reason', clean_reason.encode('ascii', 'replace').decode('ascii'))
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
    try:
        initialize_server()
        run()
    except Exception as e:
        import traceback
        with open("server_crash.log", "w", encoding="utf-8") as f:
            f.write(traceback.format_exc())
        print(f"\n[КРИТИЧЕСКАЯ ОШИБКА] Сервер упал / [CRITICAL ERROR] Server crashed:\n{e}\n")
        print("Подробности сохранены в / Details saved to: server_crash.log")
        input("Нажмите Enter для выхода... / Press Enter to exit...")
