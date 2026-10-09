using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MoniMS.App.Imaging;

/// <summary>불러온 이미지 (모두 Freeze됨). GIF 애니메이션이면 Frames/Delays에 완성된 프레임이 들어 있다.</summary>
public sealed record LoadedImage(IReadOnlyList<BitmapSource> Frames, IReadOnlyList<TimeSpan> Delays)
{
    public BitmapSource First => Frames[0];
    public bool IsAnimated => Frames.Count > 1;

    /// <summary>가로 / 세로 (화면에 보이는 방향 기준).</summary>
    public double AspectRatio => First.PixelHeight == 0 ? 1 : (double)First.PixelWidth / First.PixelHeight;
}

/// <summary>
/// 사진/GIF 불러오기. 파일은 메모리로 읽어서 잠그지 않는다.
/// - 사진: 휴대폰 사진의 회전 정보(EXIF)를 반영하고, 너무 크면 줄여서 디코딩한다.
/// - GIF: 프레임마다 위치·투명색·이전 프레임 처리 방식(disposal)을 반영해 완성된 프레임을 미리 만든다.
///   프레임 전체 크기가 메모리 예산을 넘으면 그만큼 줄인다.
/// </summary>
public static class ImageLoader
{
    private const int MaxSide = 2048;
    private const int MaxFrames = 1000;
    private const long AnimationBudgetBytes = 160L * 1024 * 1024;

    /// <summary>브라우저와 같은 규칙: 지연이 0~10ms로 적힌 GIF는 100ms로 재생.</summary>
    private static readonly TimeSpan DefaultDelay = TimeSpan.FromMilliseconds(100);

    /// <summary>화면에 보일 크기(픽셀, 회전 반영). 머리말만 읽어서 빠르다. 이미지가 아니면 null.</summary>
    public static (int Width, int Height)? ReadSize(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
            if (decoder is GifBitmapDecoder && GifScreenSize(decoder) is { } screen)
                return screen;
            var frame = decoder.Frames[0];
            return ReadOrientation(frame) >= 5 ? (frame.PixelHeight, frame.PixelWidth) : (frame.PixelWidth, frame.PixelHeight);
        }
        catch (Exception)
        {
            return null; // 손상됐거나 지원하지 않는 형식
        }
    }

    /// <summary>백그라운드에서 불러온다. 실패하면 null.</summary>
    public static Task<LoadedImage?> LoadAsync(string path) => Task.Run(() => Load(path));

    public static LoadedImage? Load(string path)
    {
        try
        {
            var bytes = File.ReadAllBytes(path);
            if (IsGif(bytes) && LoadGif(bytes) is { } animated)
                return animated;
            return new LoadedImage([LoadStill(bytes, MaxSide)], [TimeSpan.Zero]);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// 애니메이션 프레임을 화면에 그릴 픽셀 크기로 미리 맞춘다. 렌더링 때 프레임마다 보간하는 비용이
    /// (소프트웨어 렌더링에서) 대부분이라, 한 번 맞춰 두면 GIF 재생 CPU가 크게 준다.
    /// 메모리가 너무 많이 들면(작은 GIF를 크게 늘릴 때) null → 원래 프레임을 실시간 보간.
    /// </summary>
    public static LoadedImage? ScaleFrames(LoadedImage image, int width, int height)
    {
        var first = image.First;
        if (width <= 0 || height <= 0 || (long)width * height * 4 * image.Frames.Count > ScaledBudgetBytes)
            return null;
        if (Math.Abs(first.PixelWidth - width) <= 1 && Math.Abs(first.PixelHeight - height) <= 1)
            return image;
        var transform = new ScaleTransform((double)width / first.PixelWidth, (double)height / first.PixelHeight);
        transform.Freeze();
        var frames = image.Frames.Select(f =>
        {
            BitmapSource scaled = new CachedBitmap(new TransformedBitmap(f, transform), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            scaled.Freeze();
            return scaled;
        }).ToList();
        return image with { Frames = frames };
    }

    private const long ScaledBudgetBytes = 128L * 1024 * 1024;

    /// <summary>미리보기용 작은 이미지 (GIF는 첫 프레임, 회전 반영). 실패하면 null.</summary>
    public static BitmapSource? LoadThumbnail(string path, int maxSide)
    {
        try
        {
            return File.Exists(path) ? LoadStill(File.ReadAllBytes(path), maxSide) : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static bool IsGif(byte[] bytes) =>
        bytes.Length > 6 && bytes[0] == 'G' && bytes[1] == 'I' && bytes[2] == 'F';

    private static BitmapSource LoadStill(byte[] bytes, int maxSide)
    {
        int width, height, orientation;
        using (var probe = new MemoryStream(bytes))
        {
            var frame = BitmapDecoder.Create(probe, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None).Frames[0];
            (width, height, orientation) = (frame.PixelWidth, frame.PixelHeight, ReadOrientation(frame));
        }

        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = new MemoryStream(bytes);
        if (Math.Max(width, height) > maxSide)
        {
            // 큰 사진은 디코딩할 때 줄인다 (JPEG은 이쪽이 훨씬 빠르고 메모리도 적다)
            if (width >= height)
                image.DecodePixelWidth = maxSide;
            else
                image.DecodePixelHeight = maxSide;
        }
        image.EndInit();

        var result = Orient(image, orientation);
        result.Freeze();
        return result;
    }

    /// <summary>EXIF 회전값(1~8)대로 돌리거나 뒤집는다.</summary>
    private static BitmapSource Orient(BitmapSource source, int orientation)
    {
        Transform? transform = orientation switch
        {
            2 => new ScaleTransform(-1, 1),
            3 => new RotateTransform(180),
            4 => new ScaleTransform(1, -1),
            5 => new TransformGroup { Children = { new ScaleTransform(-1, 1), new RotateTransform(270) } },
            6 => new RotateTransform(90),
            7 => new TransformGroup { Children = { new ScaleTransform(-1, 1), new RotateTransform(90) } },
            8 => new RotateTransform(270),
            _ => null,
        };
        return transform is null ? source : new CachedBitmap(new TransformedBitmap(source, transform), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
    }

    private static int ReadOrientation(BitmapFrame frame)
    {
        try
        {
            return frame.Metadata is BitmapMetadata m && m.GetQuery("System.Photo.Orientation") is ushort o && o is >= 1 and <= 8 ? o : 1;
        }
        catch (Exception ex) when (ex is NotSupportedException or InvalidOperationException or ArgumentException)
        {
            return 1; // PNG 등 EXIF가 없는 형식
        }
    }

    // ---------------- GIF ----------------

    private static LoadedImage? LoadGif(byte[] bytes)
    {
        var decoder = new GifBitmapDecoder(new MemoryStream(bytes), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        if (decoder.Frames.Count <= 1)
            return null; // 정지 GIF → 일반 사진처럼

        var (w, h) = GifScreenSize(decoder) ?? (decoder.Frames[0].PixelWidth, decoder.Frames[0].PixelHeight);
        var count = Math.Min(decoder.Frames.Count, MaxFrames);
        var scale = Math.Min(1.0, Math.Min(
            Math.Sqrt(AnimationBudgetBytes / ((double)w * h * 4 * count)),
            (double)MaxSide / Math.Max(w, h)));

        var canvas = new byte[w * h * 4]; // 지금까지 그려진 화면 (Pbgra32, 처음엔 투명)
        var frames = new List<BitmapSource>(count);
        var delays = new List<TimeSpan>(count);
        var previousRect = Int32Rect.Empty;
        var previousDisposal = 0;
        byte[]? saved = null;

        for (var i = 0; i < count; i++)
        {
            var frame = decoder.Frames[i];
            var meta = frame.Metadata as BitmapMetadata;
            var left = Query(meta, "/imgdesc/Left");
            var top = Query(meta, "/imgdesc/Top");
            var delay = Query(meta, "/grctlext/Delay");
            var disposal = Query(meta, "/grctlext/Disposal");

            // 이전 프레임을 다 보여준 뒤의 처리: 2 = 그 자리를 투명하게, 3 = 그리기 전 상태로
            if (previousDisposal == 2)
                Fill(canvas, w, previousRect, null);
            else if (previousDisposal == 3 && saved is not null)
                Fill(canvas, w, previousRect, saved);

            var rect = Clip(new Int32Rect(left, top, frame.PixelWidth, frame.PixelHeight), w, h);
            saved = disposal == 3 ? Copy(canvas, w, rect) : null;
            Draw(canvas, w, frame, left, top, rect);

            frames.Add(Snapshot(canvas, w, h, scale));
            delays.Add(delay <= 1 ? DefaultDelay : TimeSpan.FromMilliseconds(delay * 10));
            previousRect = rect;
            previousDisposal = disposal;
        }
        return new LoadedImage(frames, delays);
    }

    private static (int, int)? GifScreenSize(BitmapDecoder decoder)
    {
        try
        {
            if (decoder.Metadata is { } m)
            {
                var w = Query(m, "/logscrdesc/Width");
                var h = Query(m, "/logscrdesc/Height");
                if (w > 0 && h > 0)
                    return (w, h);
            }
        }
        catch (NotSupportedException)
        {
        }
        return null;
    }

    private static int Query(BitmapMetadata? meta, string query)
    {
        try
        {
            return meta?.GetQuery(query) is { } value ? Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture) : 0;
        }
        catch (Exception ex) when (ex is NotSupportedException or InvalidCastException or FormatException or OverflowException or ArgumentException)
        {
            return 0;
        }
    }

    private static Int32Rect Clip(Int32Rect r, int w, int h)
    {
        var x = Math.Clamp(r.X, 0, w);
        var y = Math.Clamp(r.Y, 0, h);
        return new Int32Rect(x, y, Math.Clamp(r.X + r.Width, x, w) - x, Math.Clamp(r.Y + r.Height, y, h) - y);
    }

    /// <summary>프레임을 캔버스에 덮어 그린다. 투명색 픽셀은 아래(이전 프레임)가 보이도록 건너뛴다.</summary>
    private static void Draw(byte[] canvas, int canvasWidth, BitmapSource frame, int left, int top, Int32Rect rect)
    {
        if (rect.Width == 0 || rect.Height == 0)
            return;
        var fw = frame.PixelWidth;
        var pixels = new byte[fw * frame.PixelHeight * 4];
        new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0).CopyPixels(pixels, fw * 4, 0);
        for (var y = rect.Y; y < rect.Y + rect.Height; y++)
        {
            var src = ((y - top) * fw + (rect.X - left)) * 4;
            var dst = (y * canvasWidth + rect.X) * 4;
            for (var x = 0; x < rect.Width; x++, src += 4, dst += 4)
            {
                if (pixels[src + 3] == 0)
                    continue;
                canvas[dst] = pixels[src];
                canvas[dst + 1] = pixels[src + 1];
                canvas[dst + 2] = pixels[src + 2];
                canvas[dst + 3] = 255; // GIF는 완전 투명/불투명뿐
            }
        }
    }

    private static byte[] Copy(byte[] canvas, int canvasWidth, Int32Rect rect)
    {
        var data = new byte[rect.Width * rect.Height * 4];
        for (var y = 0; y < rect.Height; y++)
            Buffer.BlockCopy(canvas, ((rect.Y + y) * canvasWidth + rect.X) * 4, data, y * rect.Width * 4, rect.Width * 4);
        return data;
    }

    /// <summary>영역을 data로 되돌린다 (null이면 투명하게).</summary>
    private static void Fill(byte[] canvas, int canvasWidth, Int32Rect rect, byte[]? data)
    {
        for (var y = 0; y < rect.Height; y++)
        {
            var dst = ((rect.Y + y) * canvasWidth + rect.X) * 4;
            if (data is null)
                Array.Clear(canvas, dst, rect.Width * 4);
            else
                Buffer.BlockCopy(data, y * rect.Width * 4, canvas, dst, rect.Width * 4);
        }
    }

    private static BitmapSource Snapshot(byte[] canvas, int w, int h, double scale)
    {
        // 투명 픽셀은 0,0,0,0 이고 나머지는 불투명이라 Pbgra32(렌더링에 바로 쓰는 형식)로 그대로 쓸 수 있다
        BitmapSource bitmap = BitmapSource.Create(w, h, 96, 96, PixelFormats.Pbgra32, null, canvas, w * 4);
        if (scale < 0.999)
            bitmap = new CachedBitmap(new TransformedBitmap(bitmap, new ScaleTransform(scale, scale)), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        bitmap.Freeze();
        return bitmap;
    }
}
