using System.Buffers.Binary;
using Lightflow.UI;
using LightflowStudio;
using SkiaSharp;
using Xunit;
namespace Lightflow.UI.Tests;

public sealed class ThumbnailCodecTests
{
    private static string Root(){var path=Path.Combine(Path.GetTempPath(),"lf036-codec-"+Guid.NewGuid());Directory.CreateDirectory(path);return path;}
    [Theory]
    [InlineData(1,0,1,2,3)] [InlineData(2,1,0,3,2)] [InlineData(3,3,2,1,0)] [InlineData(4,2,3,0,1)]
    [InlineData(5,0,2,1,3)] [InlineData(6,2,0,3,1)] [InlineData(7,3,1,2,0)] [InlineData(8,1,3,0,2)]
    public void AllExifOrientationsPreserveCornerIdentity(int orientation,int tl,int tr,int bl,int br)
    {
        var root=Root();try
        {
            var source=Path.Combine(root,"source.jpg");var target=Path.Combine(root,"output.jpg");
            using var bitmap=new SKBitmap(120,80);SKColor[] colors=[SKColors.Red,SKColors.Lime,SKColors.Blue,SKColors.Yellow];
            for(int y=0;y<80;y++)for(int x=0;x<120;x++)bitmap.SetPixel(x,y,colors[(y>=40?2:0)+(x>=60?1:0)]);
            using var image=SKImage.FromBitmap(bitmap);using var data=image.Encode(SKEncodedImageFormat.Jpeg,100);var bytes=data.ToArray();
            byte[] exif=[69,120,105,102,0,0,73,73,42,0,8,0,0,0,1,0,18,1,3,0,1,0,0,0,(byte)orientation,0,0,0,0,0,0,0];
            using(var file=File.Create(source)){file.Write(bytes.AsSpan(0,2));file.Write([255,225,0,(byte)(exif.Length+2)]);file.Write(exif);file.Write(bytes.AsSpan(2));}
            var codec=new ThumbnailImageCodec();Assert.Equal(ThumbnailGenerationStatus.Succeeded,codec.Render(source,target,[],default).Status);
            using var output=SKBitmap.Decode(target);Assert.Equal(orientation>=5?80:120,output.Width);Assert.Equal(orientation>=5?120:80,output.Height);
            var actual=new[]{output.GetPixel(10,10),output.GetPixel(output.Width-10,10),output.GetPixel(10,output.Height-10),output.GetPixel(output.Width-10,output.Height-10)};
            var expected=new[]{tl,tr,bl,br};for(int i=0;i<4;i++)Assert.True(Distance(actual[i],colors[expected[i]])<30,$"orientation {orientation}, corner {i}: {actual[i]}");
        }finally{Directory.Delete(root,true);}
    }
    [Fact]
    public void AlphaIsCompositedAndOrderedLutsAreAppliedBeforeCacheValidation()
    {
        var root=Root();try
        {
            var source=Path.Combine(root,"alpha.png");var target=Path.Combine(root,"output.jpg");
            using(var bitmap=new SKBitmap(new SKImageInfo(32,32,SKColorType.Rgba8888,SKAlphaType.Unpremul))){bitmap.Erase(new SKColor(200,100,50,0));using var image=SKImage.FromBitmap(bitmap);using var data=image.Encode(SKEncodedImageFormat.Png,100);File.WriteAllBytes(source,data.ToArray());}
            var codec=new ThumbnailImageCodec();codec.Render(source,target,[],default);using(var pixels=SKBitmap.Decode(target))Assert.True(Distance(pixels.GetPixel(16,16),new SKColor(5,7,10))<10);
            File.Delete(target);
            var camera=Path.Combine(root,"camera.cube");var creative=Path.Combine(root,"creative.cube");
            // First stage sets red. Second stage maps red to green. Reversing the stages would produce red.
            File.WriteAllText(camera,"LUT_3D_SIZE 2\n"+string.Concat(Enumerable.Repeat("1 0 0\n",8)));
            File.WriteAllText(creative,"LUT_3D_SIZE 2\n"+string.Concat(Enumerable.Range(0,8).Select(i=>$"0 {i%2} 0\n")));
            Assert.Equal(ThumbnailGenerationStatus.Succeeded,codec.Render(source,target,[camera,creative],default).Status);
            using(var pixels=SKBitmap.Decode(target))Assert.True(Distance(pixels.GetPixel(16,16),SKColors.Lime)<20);
            Assert.True(codec.Validate(target));File.WriteAllBytes(target,File.ReadAllBytes(target)[..40]);Assert.False(codec.Validate(target));
            File.Delete(target);Assert.ThrowsAny<IOException>(()=>codec.Render(source,target,[Path.Combine(root,"missing.cube")],default));Assert.False(File.Exists(target));
        }finally{Directory.Delete(root,true);}
    }
    [Fact]
    public void EmbeddedLinearProfileIsConvertedToSrgb()
    {
        var root=Root();try
        {
            var source=Path.Combine(root,"linear.png");var target=Path.Combine(root,"output.jpg");
            using var space=SKColorSpace.CreateSrgbLinear();using(var bitmap=new SKBitmap(new SKImageInfo(32,32,SKColorType.Rgba8888,SKAlphaType.Opaque,space)))
            {for(int y=0;y<32;y++)for(int x=0;x<32;x++)System.Runtime.InteropServices.Marshal.WriteInt32(bitmap.GetPixels(),(y*32+x)*4,unchecked((int)0xff808080));using var image=SKImage.FromBitmap(bitmap);using var data=image.Encode(SKEncodedImageFormat.Png,100);File.WriteAllBytes(source,data.ToArray());}
            using(var sourceCodec=SKCodec.Create(source))Assert.True(sourceCodec.Info.ColorSpace?.GammaIsLinear,"Generated fixture must retain its linear profile.");
            new ThumbnailImageCodec().Render(source,target,[],default);using var pixels=SKBitmap.Decode(target);Assert.InRange((int)pixels.GetPixel(16,16).Red,184,192);
        }finally{Directory.Delete(root,true);}
    }
    [Fact]
    public void LargeImageOutputIsCappedAndCancellationDoesNotPublish()
    {
        var root=Root();try
        {
            var source=Path.Combine(root,"large.png");var target=Path.Combine(root,"output.jpg");using(var bitmap=new SKBitmap(1800,900)){bitmap.Erase(SKColors.Coral);using var image=SKImage.FromBitmap(bitmap);using var data=image.Encode(SKEncodedImageFormat.Png,100);File.WriteAllBytes(source,data.ToArray());}
            var codec=new ThumbnailImageCodec();codec.Render(source,target,[],default);using(var pixels=SKBitmap.Decode(target)){Assert.Equal(512,pixels.Width);Assert.Equal(256,pixels.Height);}
            File.Delete(target);using var cancel=new CancellationTokenSource();cancel.Cancel();Assert.Throws<OperationCanceledException>(()=>codec.Render(source,target,[],cancel.Token));Assert.False(File.Exists(target));
        }finally{Directory.Delete(root,true);}
    }
    private static int Distance(SKColor a,SKColor b)=>Math.Abs(a.Red-b.Red)+Math.Abs(a.Green-b.Green)+Math.Abs(a.Blue-b.Blue);
}
