"""Relocate extracted bottle Mach-O references only inside task workspace."""
import pathlib, subprocess
root = pathlib.Path(__file__).resolve().parents[2] / 'work/deps/bottles'
opt = root / 'opt'; opt.mkdir(exist_ok=True)
for formula in root.iterdir():
    if not formula.is_dir() or formula==opt: continue
    versions=[p for p in formula.iterdir() if p.is_dir()]
    if versions and not (opt/formula.name).exists(): (opt/formula.name).symlink_to(versions[0])
files = set()
lib = root.parent / 'lib'; lib.mkdir(exist_ok=True)
for p in root.rglob('*'):
    if p.is_file() and p.is_symlink() and '.dylib' in p.name and not (lib/p.name).exists():
        (lib/p.name).symlink_to(p.resolve())
    if p.is_file() and not p.is_symlink() and ('.dylib' in p.name or p.parent.name=='bin'):
        if p.read_bytes()[:4] in (b'\xcf\xfa\xed\xfe', b'\xca\xfe\xba\xbe'):
            files.add(p)
            if '.dylib' in p.name and not (lib/p.name).exists(): (lib/p.name).symlink_to(p)
for p in files:
    lines=subprocess.check_output(['otool','-L',str(p)],text=True).splitlines()[1:]
    args=['install_name_tool']
    for line in lines:
        old=line.strip().split(' (')[0]
        if '@@HOMEBREW_' in old or str(root) in old:
            new='@rpath/'+pathlib.Path(old).name
        else: continue
        args+=['-change',old,new]
    if '.dylib' in p.name: args+=['-id','@rpath/'+p.name]
    if len(args)>1:
        subprocess.run(args+[str(p)],check=True,stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL)
        subprocess.run(['codesign','--force','--sign','-',str(p)],check=True,stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL)
print('Relocated',len(files),'task-owned Mach-O files')
