param(
    [Parameter(Mandatory = $true)][string]$ExecutablePath,
    [string]$IconPath
)
$ErrorActionPreference = 'Stop'
if (-not $IconPath) { $IconPath = Join-Path $PSScriptRoot '..\LightflowStudio\Assets\Branding\LightflowStudio.ico' }
# Read PE resources as data; never execute the inspected binary or modify its resources.
if (-not ('LightflowIconResourceCheck' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.IO;
using System.Runtime.InteropServices;
public static class LightflowIconResourceCheck {
    delegate bool EnumName(IntPtr module, IntPtr type, IntPtr name, IntPtr param);
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)] static extern IntPtr LoadLibraryEx(string path, IntPtr file, uint flags);
    [DllImport("kernel32.dll")] static extern bool FreeLibrary(IntPtr module);
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode)] static extern bool EnumResourceNames(IntPtr module, IntPtr type, EnumName callback, IntPtr param);
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode)] static extern IntPtr FindResource(IntPtr module, IntPtr name, IntPtr type);
    [DllImport("kernel32.dll")] static extern uint SizeofResource(IntPtr module, IntPtr resource);
    [DllImport("kernel32.dll")] static extern IntPtr LoadResource(IntPtr module, IntPtr resource);
    [DllImport("kernel32.dll")] static extern IntPtr LockResource(IntPtr resource);
    static byte[] Read(IntPtr module, IntPtr name, int type) {
        var resource = FindResource(module, name, (IntPtr)type);
        if (resource == IntPtr.Zero) throw new Exception("Missing icon resource");
        var bytes = new byte[SizeofResource(module, resource)];
        Marshal.Copy(LockResource(LoadResource(module, resource)), bytes, 0, bytes.Length);
        return bytes;
    }
    public static string Verify(string executable, string icon) {
        var expected = File.ReadAllBytes(icon);
        var module = LoadLibraryEx(Path.GetFullPath(executable), IntPtr.Zero, 2);
        if (module == IntPtr.Zero) throw new System.ComponentModel.Win32Exception();
        try {
            byte[] group = null;
            EnumName callback = (m,t,n,p) => { if (group == null) group = Read(m,n,14); return true; };
            EnumResourceNames(module, (IntPtr)14, callback, IntPtr.Zero);
            if (group == null) throw new Exception("Missing executable icon group");
            int count = BitConverter.ToUInt16(expected,4);
            if (count != BitConverter.ToUInt16(group,4)) throw new Exception("Icon frame count differs");
            var sizes = new string[count];
            for (int i=0; i<count; i++) {
                int e=6+i*16, g=6+i*14;
                for (int j=0;j<12;j++) if(expected[e+j]!=group[g+j]) throw new Exception("Icon frame metadata differs");
                int length=BitConverter.ToInt32(expected,e+8), offset=BitConverter.ToInt32(expected,e+12);
                var actual=Read(module,(IntPtr)BitConverter.ToUInt16(group,g+12),3);
                if (actual.Length!=length) throw new Exception("Icon frame length differs");
                for(int j=0;j<length;j++) if(actual[j]!=expected[offset+j]) throw new Exception("Embedded icon artwork differs");
                sizes[i]=(expected[e]==0 ? 256 : expected[e]).ToString();
            }
            return "Approved executable icon verified byte-for-byte: " + String.Join(", ",sizes) + " px";
        } finally { FreeLibrary(module); }
    }
}
'@
}
Write-Host ([LightflowIconResourceCheck]::Verify($ExecutablePath, $IconPath)) -ForegroundColor Green
