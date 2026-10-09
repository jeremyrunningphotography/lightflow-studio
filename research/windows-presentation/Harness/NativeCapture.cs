using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.VisualTree;

namespace WindowsPresentation;

sealed record WindowPixels(int Width,int Height,byte[] Bytes)
{
    public bool ColorNear(int x,int y,int r,int g,int b) {
        if(x<0||y<0||x>=Width||y>=Height) return false;
        int i=(y*Width+x)*4;
        return Math.Abs(Bytes[i]-b)<=2 && Math.Abs(Bytes[i+1]-g)<=2 && Math.Abs(Bytes[i+2]-r)<=2;
    }
    public void Save(string path)=>File.WriteAllBytes(path,Bytes);
}
static class NativeCapture
{
    [StructLayout(LayoutKind.Sequential)] struct Rect {public int Left,Top,Right,Bottom;}
    [StructLayout(LayoutKind.Sequential)] struct Info { public uint Size;public int Width,Height;public ushort Planes,Bits;public uint Compression,SizeImage;public int XPels,YPels;public uint Used,Important; }
    [DllImport("user32.dll")] static extern bool GetClientRect(nint hwnd,out Rect rect);
    [DllImport("user32.dll")] static extern nint GetDC(nint hwnd);
    [DllImport("user32.dll")] static extern int ReleaseDC(nint hwnd,nint dc);
    [DllImport("user32.dll")] static extern bool PrintWindow(nint hwnd,nint dc,uint flags);
    [DllImport("gdi32.dll")] static extern nint CreateCompatibleDC(nint dc);
    [DllImport("gdi32.dll")] static extern nint CreateCompatibleBitmap(nint dc,int width,int height);
    [DllImport("gdi32.dll")] static extern nint SelectObject(nint dc,nint obj);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(nint obj);
    [DllImport("gdi32.dll")] static extern bool DeleteDC(nint dc);
    [DllImport("gdi32.dll")] static extern int GetDIBits(nint dc,nint bitmap,uint start,uint rows,[Out]byte[] bits,ref Info info,uint usage);
    public static WindowPixels Capture(Window window)
    {
        nint hwnd=window.TryGetPlatformHandle()!.Handle;
        if(!GetClientRect(hwnd,out var rect))throw new Exception("GetClientRect");
        int width=rect.Right,height=rect.Bottom;
        var dc=GetDC(hwnd);var mem=CreateCompatibleDC(dc);var bitmap=CreateCompatibleBitmap(dc,width,height);var old=SelectObject(mem,bitmap);
        try {
            if(!PrintWindow(hwnd,mem,3))throw new Exception("PrintWindow failed");
            SelectObject(mem,old);
            var bytes=new byte[width*height*4];var info=new Info {Size=40,Width=width,Height=-height,Planes=1,Bits=32};
            if(GetDIBits(dc,bitmap,0,(uint)height,bytes,ref info,0)!=height)throw new Exception("GetDIBits failed");
            return new(width,height,bytes);
        } finally {SelectObject(mem,old);DeleteObject(bitmap);DeleteDC(mem);ReleaseDC(hwnd,dc);}
    }
}
