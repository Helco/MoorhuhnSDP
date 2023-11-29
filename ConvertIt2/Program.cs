using System.Runtime.InteropServices;
using System.Text;
using SixLabors.ImageSharp;

namespace ConvertIt2;

internal readonly record struct Sprite(int OffX, int OffY, int Width, int Height, byte Flag);

internal class ImageFile
{
    public ImageFile(string fileName)
    {
        using var fileStream = new FileStream(fileName, FileMode.Open, FileAccess.Read);
        using var reader = new BinaryReader(fileStream);
        Width = reader.ReadInt32();
        Height = reader.ReadInt32();
        reader.BaseStream.Position += 4 * 8;
        var spriteCount = reader.ReadInt32();
        var fontOffset = reader.ReadInt32();

        Sprites = new Sprite[spriteCount];
        for (int i = 0; i < spriteCount; i++)
            Sprites[i] = new(reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(), reader.ReadByte());

        if (fontOffset > 0)
        {
            reader.BaseStream.Position = fontOffset;
            FontParams = new int[6];
            for (int i = 0; i < FontParams.Length; i++)
                FontParams[i] = reader.ReadInt32();
            var nameBytes = reader.ReadBytes(0x80);
            FontName = Encoding.Latin1.GetString(nameBytes).TrimEnd('\0');
            FontLookup = reader.ReadBytes(256);
        }

        var subFormat = reader.ReadInt32();
        Container = reader.ReadInt32();
        var imageOffset = reader.ReadInt32();
        var imageSize = reader.ReadInt32();
        var paletteOffset = reader.ReadInt32();
        var paletteSize = reader.ReadInt32();
        ColorKey = reader.ReadUInt32();

        reader.BaseStream.Position = imageOffset;
        ImageData = reader.ReadBytes(imageSize);
        if (paletteSize > 0)
        {
            reader.BaseStream.Position = paletteOffset + 4;
            Colors = reader.ReadBytes(paletteSize - 4);
        }

        if (Container == 2)
            Image = Image.Load(ImageData);
        else if (Container == 1)
            Image = ImportRLE(subFormat);
        else
            throw new Exception("Unknown container format");
    }

    public int Container;
    public int Width, Height;
    public int[]? FontParams = null;
    public string? FontName = null;
    public byte[]? FontLookup = null;
    public Sprite[] Sprites;
    public byte[] ImageData;
    public byte[]? Colors;
    public uint ColorKey;
    public Image Image;

    private Image ImportRLE(int subFormat)
    {
        ReadOnlySpan<byte> data = ImageData.AsSpan();
        var bpp = Read<byte>(ref data);
        var width2 = Read<int>(ref data);
        var height2 = Read<int>(ref data);
        if (Width != width2 || Height != height2)
            throw new InvalidDataException("Unexpectedly inner size does not match outer size");

        var decoded = new byte[Width * Height * bpp];
        var rowSpan = decoded.AsSpan(0, Width * bpp);

        int y = 0;
        while(true)
        {
            var packet = Read<byte>(ref data);
            if (packet == 0x81)
                throw new InvalidDataException("Invalid packet header");
            else if (packet == 0x80)
            {
                if (++y >= Height)
                    break;
                rowSpan = decoded.AsSpan(y * Width * bpp, Width * bpp);
            }
            else if (packet > 0x80)
            {
                packet &= 0x7F;
                var pixel = Read(ref data, bpp);
                for (int i = 0; i < packet; i++)
                    Write(ref rowSpan, pixel);
            }
            else
            {
                var pixel = Read(ref data, bpp * (packet + 1));
                Write(ref rowSpan, pixel);
            }
        }

        var pixels = decoded;
        if (bpp == 1)
        {
            pixels = new byte[Width * Height * 3];
            for (int i = 0; i < Width * Height; i++)
                Colors.AsSpan(decoded[i] * 3, 3).CopyTo(pixels.AsSpan(i * 3));
        }

        return Image.LoadPixelData<SixLabors.ImageSharp.PixelFormats.Rgb24>(pixels, Width, Height);
    }

    private unsafe static T Read<T>(ref ReadOnlySpan<byte> data) where T : unmanaged
    {
        var data2 = MemoryMarshal.Cast<byte, T>(data);
        var result = data2[0];
        data = data[sizeof(T)..];
        return result;
    }

    private static void Write(ref Span<byte> output, ReadOnlySpan<byte> input)
    {
        input.CopyTo(output);
        output = output[input.Length..];
    }
    
    private static ReadOnlySpan<byte> Read(ref ReadOnlySpan<byte> data, int size)
    {
        var chunk = data[..size];
        data = data[size..];
        return chunk;
    }
}

internal class Program
{
    static void Main(string[] args)
    {
        Console.WriteLine("Hello, World!");
        var path = @"C:\dev\moorhuhnsdp\MoorhuhnSDP\ExtractArchiveCs\bin\Debug\netcoreapp3.1\out\";
        var allImages = Directory.GetFiles(path, "*._it2", SearchOption.AllDirectories);
        var success = 0;
        for (int i = 0; i < allImages.Length; i++)
        {
            var imagePath = allImages[i];
            var outputPath = Path.ChangeExtension(Path.GetRelativePath(path, imagePath), "png");
            Console.Write($"{i}/{allImages.Length}:\t");
            Console.Write(outputPath + "...");
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            try
            {
                var imageFile = new ImageFile(imagePath);
                if (imageFile.Container == 1)
                    imageFile.Image.SaveAsPng(outputPath);
                else
                    File.WriteAllBytes(Path.ChangeExtension(outputPath, "jpg"), imageFile.ImageData);
                Console.WriteLine("done");
                success++;
            }
            catch(Exception e)
            {
                Console.WriteLine(e.Message);
            }
        }
        Console.WriteLine($"Converted {success}/{allImages.Length} ({success * 100 / allImages.Length}%) images");
    }


}
