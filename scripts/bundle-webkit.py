#!/usr/bin/env python3
"""Bundle Ubuntu 24.04 WebKitGTK, helper processes, resources and license notices."""
import pathlib, re, shutil, subprocess, sys
if 'VERSION_ID="24.04"' not in pathlib.Path('/etc/os-release').read_text():
    raise SystemExit('Build the WebKit runtime on Ubuntu 24.04 amd64.')
app = pathlib.Path(sys.argv[1]).resolve()
lib = app / 'usr/lib'
lib.mkdir(parents=True, exist_ok=True)
# Use the target's glibc, C++ runtime and GPU driver stack.
excluded = re.compile(r'^(ld-linux|lib(c|m|pthread|dl|rt|resolv|util|nss_[^.]+|anl|BrokenLocale)\.so|lib(GL|EGL|GLX|OpenGL|GLdispatch|vulkan|drm|gbm|stdc\+\+|gcc_s)[-.])')
queue = [pathlib.Path('/usr/lib/x86_64-linux-gnu/libwebkit2gtk-4.1.so.0')]
for tool in ['/usr/bin/secret-tool', '/usr/bin/dbus-send', '/usr/lib/x86_64-linux-gnu/gdk-pixbuf-2.0/gdk-pixbuf-query-loaders',
             '/usr/lib/x86_64-linux-gnu/libgtk-3-0t64/gtk-query-immodules-3.0']:
    source = pathlib.Path(tool)
    (app / 'usr/bin').mkdir(parents=True, exist_ok=True)
    shutil.copy2(source, app / 'usr/bin' / source.name)
    queue.append(source)
packages = set()
def provenance(path):
    result = subprocess.run(['dpkg-query', '-S', str(path.resolve())], capture_output=True, text=True)
    for line in result.stdout.splitlines():
        if ': ' in line: packages.add(line.split(': ')[0].split(':')[0])
for relative in ['webkit2gtk-4.1', 'gio/modules', 'gstreamer-1.0', 'gtk-3.0/3.0.0/immodules', 'gdk-pixbuf-2.0/2.10.0/loaders']:
    source = pathlib.Path('/usr/lib/x86_64-linux-gnu') / relative
    if not source.is_dir(): raise SystemExit(f'Missing runtime directory: {source}')
    shutil.copytree(source, lib / relative, dirs_exist_ok=True, symlinks=False)
    for path in source.rglob('*'):
        if path.is_file():
            provenance(path)
            with path.open('rb') as stream: elf = stream.read(4) == b'\x7fELF'
            if elf: queue.append(path)
seen = set()
while queue:
    source = queue.pop()
    if source in seen: continue
    seen.add(source)
    provenance(source)
    if not excluded.match(source.name): shutil.copy2(source, lib / source.name, follow_symlinks=True)
    result = subprocess.run(['ldd', str(source)], capture_output=True, text=True)
    if 'not found' in result.stdout: raise SystemExit(result.stdout)
    for line in result.stdout.splitlines():
        match = re.search(r'=> (/\S+)', line)
        if match and not excluded.match(pathlib.Path(match[1]).name): queue.append(pathlib.Path(match[1]))
for relative in ['glib-2.0/schemas', 'icons/Adwaita', 'icons/hicolor', 'mime', 'webkitgtk-4.1']:
    source = pathlib.Path('/usr/share') / relative
    if source.exists(): shutil.copytree(source, app / 'usr/share' / relative, dirs_exist_ok=True, symlinks=False)
subprocess.run(['glib-compile-schemas', str(app / 'usr/share/glib-2.0/schemas')], check=True)
# Keep this stack separate: real GPUs use the host's driver libraries.
software = lib / 'software-gl'
(software / 'dri').mkdir(parents=True, exist_ok=True)
mesa = pathlib.Path('/usr/lib/x86_64-linux-gnu')
shutil.copy2(mesa / 'dri/swrast_dri.so', software / 'dri/swrast_dri.so', follow_symlinks=True)
software_queue = [mesa / 'dri/swrast_dri.so', mesa / 'libEGL.so.1', mesa / 'libEGL_mesa.so.0']
software_seen = set()
system_runtime = re.compile(r'^(ld-linux|lib(c|m|pthread|dl|rt|resolv|util|nss_[^.]+|anl|BrokenLocale)\.so|lib(stdc\+\+|gcc_s)[-.])')
while software_queue:
    source = software_queue.pop()
    if source in software_seen: continue
    software_seen.add(source)
    provenance(source)
    if not system_runtime.match(source.name): shutil.copy2(source, software / source.name, follow_symlinks=True)
    result = subprocess.run(['ldd', str(source)], capture_output=True, text=True)
    if 'not found' in result.stdout: raise SystemExit(result.stdout)
    for line in result.stdout.splitlines():
        match = re.search(r'=> (/\S+)', line)
        if match and not system_runtime.match(pathlib.Path(match[1]).name): software_queue.append(pathlib.Path(match[1]))
vendor = app / 'usr/share/glvnd/egl_vendor.d'
vendor.mkdir(parents=True, exist_ok=True)
shutil.copy2('/usr/share/glvnd/egl_vendor.d/50_mesa.json', vendor / '50_mesa.json')
provenance(pathlib.Path('/usr/share/glvnd/egl_vendor.d/50_mesa.json'))
notices = app / 'usr/share/licenses/webkit-runtime'
notices.mkdir(parents=True, exist_ok=True)
manifest = []
for package in sorted(packages):
    manifest.append(subprocess.check_output(['dpkg-query', '-W', '-f=${binary:Package}\t${Version}\t${source:Package}\t${source:Version}\n', package], text=True))
    copyright = pathlib.Path('/usr/share/doc') / package / 'copyright'
    if copyright.exists(): shutil.copy2(copyright, notices / (package + '.copyright'))
(notices / 'packages.tsv').write_text(''.join(manifest))
(notices / 'SOURCE.txt').write_text('Ubuntu 24.04 (noble), amd64. Copyright files contain upstream license and source URLs.\nGet matching source: apt-get source PACKAGE=SOURCE_VERSION\nhttps://archive.ubuntu.com/ubuntu/pool/\nhttps://security.ubuntu.com/ubuntu/pool/\n')
shutil.copy2(__file__, notices / 'bundle-webkit.py')
print(f'Bundled {len(seen)} ELF files; {len(packages)} packages')
# Release WebKit ignores WEBKIT_EXEC_PATH. Relocate its compiled helper/resource
# paths to the AppDir working directory, preserving each ELF string's byte size.
for binary in lib.rglob('*'):
    if not binary.is_file() or '.so' not in binary.name or 'webkit' not in binary.name.lower(): continue
    data = binary.read_bytes()
    for old, new in [(b'/usr/lib/x86_64-linux-gnu/webkit2gtk-4.1', b'./usr/lib/webkit2gtk-4.1'),
                     (b'/usr/share/webkitgtk-4.1', b'usr/share/webkitgtk-4.1')]:
        if len(new) > len(old): continue
        def relocate(match):
            value = new + match[0][len(old):]
            return value + b'\0' * (len(match[0]) - len(value))
        data = re.sub(re.escape(old) + rb'[^\x00]*\x00', relocate, data)
    binary.write_bytes(data)
