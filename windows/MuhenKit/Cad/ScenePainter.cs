using System.Numerics;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Geometry;
using MuhenKit.Core;
using Windows.UI;

namespace MuhenKit.Cad;

/// <summary>Sahneyi ekran dışında resme çizer (proje önizlemesi). Herhangi bir iş parçacığından çağrılabilir.</summary>
public static class ScenePainter
{
    public static byte[] RenderPng(CadScene s, ICollection<int> hidden, int w, int h)
    {
        var dev = CanvasDevice.GetSharedDevice();
        using var rt = new CanvasRenderTarget(dev, w, h, 96);
        using (var ds = rt.CreateDrawingSession())
        {
            ds.Clear(Color.FromArgb(255, 11, 14, 16));
            float sw = Math.Max(s.MaxX - s.MinX, 1e-3f), sh = Math.Max(s.MaxY - s.MinY, 1e-3f);
            float pad = 6, k = Math.Min((w - 2 * pad) / sw, (h - 2 * pad) / sh);
            var m = Matrix3x2.CreateTranslation(-(s.MinX + s.MaxX) / 2, -(s.MinY + s.MaxY) / 2) * Matrix3x2.CreateScale(k, -k)
                  * Matrix3x2.CreateTranslation(w / 2f, h / 2f);
            ds.Transform = m;
            var v = s.Vertices;
            foreach (var f in s.Fills)
            {
                if (hidden.Contains(f.Layer)) continue;
                using var pb = new CanvasPathBuilder(dev);
                pb.SetFilledRegionDetermination(CanvasFilledRegionDetermination.Alternate);
                for (int q = 0; q < f.LoopFirst.Length; q++)
                {
                    int a = f.LoopFirst[q];
                    pb.BeginFigure(v[a * 2], v[a * 2 + 1]);
                    for (int i = 1; i < f.LoopCount[q]; i++) pb.AddLine(v[(a + i) * 2], v[(a + i) * 2 + 1]);
                    pb.EndFigure(CanvasFigureLoop.Closed);
                }
                using var g = CanvasGeometry.CreatePath(pb);
                var c = Adapt(f.Rgb);
                c.A = (byte)(255 * f.Alpha);
                ds.FillGeometry(g, c);
            }
            var hair = new CanvasStrokeStyle { TransformBehavior = CanvasStrokeTransformBehavior.Hairline };
            foreach (var b in s.Batches)
            {
                if (hidden.Contains(b.Layer) || b.Count < 2) continue;
                using var pb = new CanvasPathBuilder(dev);
                for (int i = b.First; i + 1 < b.First + b.Count; i += 2)
                {
                    pb.BeginFigure(v[i * 2], v[i * 2 + 1]);
                    pb.AddLine(v[i * 2 + 2], v[i * 2 + 3]);
                    pb.EndFigure(CanvasFigureLoop.Open);
                }
                using var g = CanvasGeometry.CreatePath(pb);
                ds.DrawGeometry(g, Adapt(b.Rgb), 1, hair);
            }
        }
        using var ms = new MemoryStream();
        rt.SaveAsync(ms.AsRandomAccessStream(), CanvasBitmapFileFormat.Png).AsTask().GetAwaiter().GetResult();
        return ms.ToArray();
    }

    private static Color Adapt(uint rgb)
    {
        byte r = (byte)(rgb >> 16), g = (byte)(rgb >> 8), b = (byte)rgb;
        return r * 299 + g * 587 + b * 114 < 40_000 ? Color.FromArgb(255, 255, 255, 255) : Color.FromArgb(255, r, g, b);
    }
}
