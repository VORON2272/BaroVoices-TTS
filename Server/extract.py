import io

file_path = "c:/Program Files (x86)/Steam/steamapps/common/Barotrauma/LocalMods/Barotrauma-AI-Play/Исходник/SoundproofWalls.cs"

for enc in ['utf-8-sig', 'utf-8', 'utf-16', 'utf-16-le', 'cp1251']:
    try:
        with io.open(file_path, encoding=enc) as f:
            lines = f.readlines()
            break
    except Exception as e:
        pass

in_method = False
braces = 0
out = []
for l in lines:
    if "SPW_TogglePauseMenu" in l and "public static void" in l:
        in_method = True
    if in_method:
        out.append(l.rstrip())
        if "{" in l: braces += l.count("{")
        if "}" in l: braces -= l.count("}")
        if braces == 0 and "{" not in out[0] and len(out) > 2:
            break

print("\n".join(out))
