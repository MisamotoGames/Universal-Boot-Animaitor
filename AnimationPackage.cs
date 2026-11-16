using System.Drawing.Imaging;

namespace BasStudio;

internal static class AnimationPackage
{
    private static readonly ImageCodecInfo JpegCodec = ImageCodecInfo.GetImageEncoders().Single(x => x.MimeType == "image/jpeg");
    private static readonly byte[] Magic = "BASANIM1"u8.ToArray();

    public static long EstimateBytes(IReadOnlyList<string> images, long quality, Size? outputSize)
    {
        if (images.Count is < 1 or > 600)
            throw new InvalidDataException("UEFI animations must contain between 1 and 600 frames.");
        long bytes = 20; // package header
        foreach (string path in images)
        {
            var frame = EncodeFrame(path, quality, outputSize);
            bytes = checked(bytes + 12L + frame.Jpeg.Length);
            if (bytes > uint.MaxValue)
                throw new InvalidDataException("The animation package would exceed FAT32's 4 GiB single-file limit. Reduce its duration, dimensions, or JPEG quality.");
        }
        return bytes;
    }

    public static long Write(IReadOnlyList<string> images, string destination, int fps, long quality, Size? outputSize)
    {
        if (images.Count is < 1 or > 600)
            throw new InvalidDataException("UEFI animations must contain between 1 and 600 frames.");
        string temporary = destination + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 1024, FileOptions.SequentialScan))
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(Magic);
                writer.Write(1u); // format version
                writer.Write((uint)Math.Clamp(fps, 1, 60));
                writer.Write((uint)images.Count);
                foreach (string path in images)
                {
                    var frame = EncodeFrame(path, quality, outputSize);
                    if (stream.Position + 12L + frame.Jpeg.Length > uint.MaxValue)
                        throw new InvalidDataException("The animation package would exceed FAT32's 4 GiB single-file limit. Reduce its duration, dimensions, or JPEG quality.");
                    writer.Write((uint)frame.Width);
                    writer.Write((uint)frame.Height);
                    writer.Write((uint)frame.Jpeg.Length);
                    writer.Write(frame.Jpeg);
                }
                writer.Flush();
                stream.Flush(true);
            }
            long length = new FileInfo(temporary).Length;
            File.Move(temporary, destination, true);
            return length;
        }
        catch
        {
            try { File.Delete(temporary); } catch { }
            throw;
        }
    }

    private static (int Width, int Height, byte[] Jpeg) EncodeFrame(string path, long quality, Size? outputSize)
    {
        using var source = Image.FromFile(path);
        if (source.Width is < 1 or > 8192 || source.Height is < 1 or > 8192)
            throw new InvalidDataException($"Frame dimensions {source.Width}×{source.Height} exceed the UEFI player's 8192×8192 limit.");
        var canvas = outputSize ?? new Size(source.Width, source.Height);
        if (canvas.Width is < 1 or > 8192 || canvas.Height is < 1 or > 8192)
            throw new InvalidDataException($"Output dimensions {canvas.Width}×{canvas.Height} exceed the UEFI player's 8192×8192 limit.");
        if ((long)canvas.Width * canvas.Height > 4_194_304)
            throw new InvalidDataException($"Output dimensions {canvas.Width}×{canvas.Height} exceed the player's 4.2-megapixel memory limit. Choose 1920×1080 or smaller.");
        using var bitmap = new Bitmap(canvas.Width, canvas.Height, PixelFormat.Format24bppRgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.Black);
            graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
            float scale = Math.Min(bitmap.Width / (float)source.Width, bitmap.Height / (float)source.Height);
            int width = Math.Max(1, (int)Math.Round(source.Width * scale));
            int height = Math.Max(1, (int)Math.Round(source.Height * scale));
            var destination = new Rectangle((bitmap.Width - width) / 2, (bitmap.Height - height) / 2, width, height);
            graphics.DrawImage(source, destination);
        }
        using var encoded = new MemoryStream();
        using (var parameters = new EncoderParameters(1))
        {
            parameters.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, Math.Clamp(quality, 50, 95));
            bitmap.Save(encoded, JpegCodec, parameters);
        }
        return (bitmap.Width, bitmap.Height, encoded.ToArray());
    }
}
