using System.Runtime.InteropServices;
static class Native {
 [DllImport("x3bridge")] internal static extern IntPtr x3_windows();
 const string Lib="x3bridge";
 [DllImport(Lib)] internal static extern IntPtr x3_create(int w,int h);
 [DllImport(Lib)] internal static extern ulong x3_device(IntPtr p);
 [DllImport(Lib)] internal static extern IntPtr x3_surface(IntPtr p,int i);
 [DllImport(Lib)] internal static extern IntPtr x3_event(IntPtr p,int i,int release);
 [DllImport(Lib)] internal static extern ulong x3_value(IntPtr p,int i,int release);
 [DllImport(Lib)] internal static extern uint x3_id(IntPtr p,int i);
 [DllImport(Lib)] internal static extern ulong x3_stride(IntPtr p,int i);
 [DllImport(Lib)] internal static extern int x3_texture_identity(IntPtr p,int i);
 [DllImport(Lib)] internal static extern int x3_acquire(IntPtr p,int i,ulong v);
 [DllImport(Lib)] internal static extern int x3_produce(IntPtr p,int i,ulong v,uint serial,int delayMs);
 [DllImport(Lib)] internal static extern int x3_release(IntPtr p,int i);
 [DllImport(Lib)] internal static extern void x3_cancel(IntPtr p,int i);
 [DllImport(Lib)] internal static extern ulong x3_hash(IntPtr p,int i);
 [DllImport(Lib)] internal static extern void x3_read(IntPtr p,int i,IntPtr output);
 [DllImport(Lib)] internal static extern double x3_gpu_ms(IntPtr p);
 [DllImport(Lib)] internal static extern void x3_destroy(IntPtr p);
 [DllImport(Lib)] internal static extern int x3_live_surfaces();
 [DllImport(Lib)] internal static extern int x3_live_producers();
}
