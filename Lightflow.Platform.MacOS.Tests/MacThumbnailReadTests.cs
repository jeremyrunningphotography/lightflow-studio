using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Xunit;
namespace Lightflow.Platform.MacOS.Tests;

public sealed class MacThumbnailReadTests
{
    [StructLayout(LayoutKind.Sequential)]private struct Info
    {public double Duration;public int Width,Height,Orientation,Mechanism;public long ActualValue;public int ActualScale;}
    [StructLayout(LayoutKind.Sequential)]private struct Trace
    {public double OpenMs,InitializeMs,MetadataMs,FrameMs,PixelsMs,ReadMs,TotalMs;public long BytesRead,Reads,Requests;public int Active,MetadataStatus;public long Error;}
    [DllImport("libLightflowVolumeTests.dylib")]private static extern int lf_test_thumbnail_video([MarshalAs(UnmanagedType.LPUTF8Str)]string path);
    [DllImport("libLightflowStorage.dylib")]private static extern int lf_thumbnail_profile([MarshalAs(UnmanagedType.LPUTF8Str)]string path,int mode,double requested,[MarshalAs(UnmanagedType.LPUTF8Str)]string output,IntPtr cancel,out Info info,out Trace trace);
    [DllImport("libLightflowStorage.dylib")]private static extern int lf_preview_readers();

    [Fact]
    public async Task TailMoovMetadataAndPosterDoNotReadTheWholePaddedMovie()
    {
        if(!OperatingSystem.IsMacOS())return;
        var root=Path.Combine(Path.GetTempPath(),"lf036-native-read-"+Guid.NewGuid());Directory.CreateDirectory(root);
        var flag=Marshal.AllocHGlobal(4);Marshal.WriteInt32(flag,0);
        try
        {
            var movie=await MakePaddedMovie(root);
            foreach(var mode in new[]{0,2})
            {
                var operation=Task.Run(()=>{var status=lf_thumbnail_profile(movie,mode,1,Path.Combine(root,"poster.png"),flag,out var info,out var trace);return(status,info,trace);});
                (int status,Info info,Trace trace) result;
                try{result=await operation.WaitAsync(TimeSpan.FromSeconds(15));}
                catch{Marshal.WriteInt32(flag,1);await operation;throw;}
                Assert.Equal(0,result.status);Assert.InRange(result.info.Duration,2.9,3.1);
                Assert.InRange(result.trace.BytesRead,1,32*1024*1024); // The old callback consumed the 64 MiB free atom, often repeatedly.
                Assert.Equal(0,lf_preview_readers());
                if(mode==2){Assert.InRange((double)result.info.ActualValue/result.info.ActualScale,.96,1.04);Assert.InRange(result.info.Width,1,512);}
            }
        }
        finally{Marshal.FreeHGlobal(flag);Directory.Delete(root,true);}
    }
    [Fact]
    public async Task CancellationDrainsChunkWorkBeforeDescriptorAndFlagRetire()
    {
        if(!OperatingSystem.IsMacOS())return;
        var root=Path.Combine(Path.GetTempPath(),"lf036-native-cancel-"+Guid.NewGuid());Directory.CreateDirectory(root);
        try
        {
            var movie=await MakePaddedMovie(root);
            for(int i=0;i<8;i++)
            {
                var flag=Marshal.AllocHGlobal(4);Marshal.WriteInt32(flag,0);
                try
                {
                    var work=Task.Run(()=>lf_thumbnail_profile(movie,2,1,Path.Combine(root,$"{i}.png"),flag,out _,out _));
                    Assert.True(SpinWait.SpinUntil(()=>lf_preview_readers()>0||work.IsCompleted,TimeSpan.FromSeconds(3)));
                    Marshal.WriteInt32(flag,1);int status;
                    try{status=await work.WaitAsync(TimeSpan.FromSeconds(10));}catch{await work;throw;}
                    Assert.Contains(status,new[]{0,1});Assert.Equal(0,lf_preview_readers());
                }
                finally{Marshal.FreeHGlobal(flag);}
            }
        }
        finally{Directory.Delete(root,true);}
    }
    private static async Task<string> MakePaddedMovie(string root)
    {
        var original=Path.Combine(root,"original.mp4");Assert.Equal(0,await Task.Run(()=>lf_test_thumbnail_video(original)));
        var bytes=await File.ReadAllBytesAsync(original);var offset=0;
        while(offset+8<=bytes.Length)
        {
            var size32=BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset,4));
            var length=size32==1?checked((int)BinaryPrimitives.ReadUInt64BigEndian(bytes.AsSpan(offset+8,8))):checked((int)size32);
            if(System.Text.Encoding.ASCII.GetString(bytes,offset+4,4)=="moov")break;
            Assert.True(length>=8);offset+=length;
        }
        Assert.InRange(offset,8,bytes.Length-8);
        var path=Path.Combine(root,"tail-moov.mp4");using var file=File.Create(path);file.Write(bytes.AsSpan(0,offset));
        const int padding=64*1024*1024;var header=new byte[8];BinaryPrimitives.WriteUInt32BigEndian(header,padding);"free"u8.CopyTo(header.AsSpan(4));file.Write(header);
        file.SetLength(file.Position+padding-8);file.Position=file.Length;file.Write(bytes.AsSpan(offset));return path;
    }
}
