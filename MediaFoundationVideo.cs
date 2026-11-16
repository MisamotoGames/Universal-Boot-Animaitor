using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Vortice.MediaFoundation;

namespace BasStudio;

internal static class MediaFoundationVideo
{
    public static (string Folder, int Width, int Height, int Frames) Decode(string path, int fps, IProgress<int>? progress = null)
    {
        const int maxFrames = 600;
        fps = Math.Clamp(fps, 1, 60);
        string folder = Path.Combine(Path.GetTempPath(), "BAS", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        MediaFactory.MFStartup(false).CheckError();
        try
        {
            using var reader = MediaFactory.MFCreateSourceReaderFromURL(path, null);
            using var type = MediaFactory.MFCreateMediaType();
            type.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Video).CheckError();
            type.Set(MediaTypeAttributeKeys.Subtype, VideoFormatGuids.Rgb32).CheckError();
            reader.SetCurrentMediaType(SourceReaderIndex.FirstVideoStream, type);
            using var currentType = reader.GetCurrentMediaType(SourceReaderIndex.FirstVideoStream);
            ulong dimensions = currentType.GetUInt64(MediaTypeAttributeKeys.FrameSize);
            int width = checked((int)(dimensions >> 32));
            int height = checked((int)(dimensions & 0xffffffff));
            if (width < 1 || height < 1 || width > 8192 || height > 8192) throw new InvalidDataException("The video dimensions are outside the supported range.");
            int signedStride = unchecked((int)currentType.GetUInt32(MediaTypeAttributeKeys.DefaultStride));
            if (signedStride == 0) signedStride = width * 4;
            bool flipRows = signedStride < 0;
            int stride = Math.Abs(signedStride);

            long firstTimestamp = -1;
            int written = 0;
            string? previous = null;
            while (true)
            {
                using var sample = reader.ReadSample(SourceReaderIndex.FirstVideoStream, SourceReaderControlFlag.None, out _, out SourceReaderFlag flags, out long timestamp);
                if ((flags & SourceReaderFlag.EndOfStream) != 0) break;
                if (sample is null) continue;
                if (firstTimestamp < 0) firstTimestamp = timestamp;
                long targetIndex = Math.Max(0, (timestamp - firstTimestamp) * fps / 10_000_000);
                if (targetIndex < written) continue;
                if (targetIndex >= maxFrames) throw new InvalidDataException($"This clip exceeds the {maxFrames}-frame limit at {fps} FPS (about {maxFrames / (double)fps:0.#} seconds). Trim the animation or lower its output FPS.");

                using var buffer = sample.ConvertToContiguousBuffer();
                buffer.Lock(out IntPtr source, out _, out int currentLength);
                byte[] rgba;
                try
                {
                    if (currentLength < stride * height) throw new InvalidDataException("The video decoder returned an incomplete image frame.");
                    rgba = new byte[stride * height];
                    Marshal.Copy(source, rgba, 0, rgba.Length);
                }
                finally { buffer.Unlock(); }

                using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppRgb);
                var rect = new Rectangle(0, 0, width, height);
                var data = bitmap.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppRgb);
                try
                {
                    for (int y = 0; y < height; y++)
                    {
                        int sourceY = flipRows ? height - 1 - y : y;
                        Marshal.Copy(rgba, sourceY * stride, IntPtr.Add(data.Scan0, y * data.Stride), Math.Min(width * 4, Math.Abs(data.Stride)));
                    }
                }
                finally { bitmap.UnlockBits(data); }

                while (written < targetIndex)
                {
                    string duplicate = Path.Combine(folder, $"{written:D5}.jpg");
                    if (previous is null) bitmap.Save(duplicate, ImageFormat.Jpeg); else File.Copy(previous, duplicate);
                    previous = duplicate; written++;
                }
                string output = Path.Combine(folder, $"{written:D5}.jpg");
                bitmap.Save(output, ImageFormat.Jpeg);
                previous = output; written++;
                progress?.Report(written);
            }
            if (written == 0) throw new InvalidDataException("No video frames were decoded. Check that the file contains a supported video stream.");
            return (folder, width, height, written);
        }
        catch
        {
            try { Directory.Delete(folder, true); } catch { }
            throw;
        }
        finally { MediaFactory.MFShutdown(); }
    }
}
