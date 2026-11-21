using System.IO.Compression;
using System.Numerics;
using System.Runtime.InteropServices;
using ImGuiNET;

namespace Diagnostics.Tests;

/// <summary>
/// An ImGui context with no window and no device, whose frames are drawn into a picture on the
/// CPU, so the tests read what the drawers draw on any machine.
/// </summary>
internal sealed class ImGuiFrames : IDisposable
{
    private readonly IntPtr _context;
    private readonly byte[] _atlas;
    private readonly int _atlasWidth;
    private readonly int _atlasHeight;

    public ImGuiFrames(int width, int height)
    {
        (Width, Height) = (width, height);
        _context = ImGui.CreateContext();
        ImGui.SetCurrentContext(_context);
        ImGui.StyleColorsLight();
        var io = ImGui.GetIO();
        io.DisplaySize = new Vector2(width, height);
        io.DeltaTime = 1f / 60f;
        // ImGui keeps a pad's buttons only where the backend says it has a pad.
        io.BackendFlags |= ImGuiBackendFlags.HasGamepad;
        // No file is written beside the tests for the windows' places.
        unsafe
        {
            io.NativePtr->IniFilename = null;
        }

        io.Fonts.GetTexDataAsRGBA32(out IntPtr pixels, out _atlasWidth, out _atlasHeight, out _);
        _atlas = new byte[_atlasWidth * _atlasHeight * 4];
        Marshal.Copy(pixels, _atlas, 0, _atlas.Length);
        io.Fonts.SetTexID(1);
    }

    public int Width { get; }

    public int Height { get; }

    public Vector2 Size => new(Width, Height);

    /// <summary>How many vertices the last frame drew.</summary>
    public int Vertices { get; private set; }

    /// <summary>The last frame as RGBA on a white ground, rows from the top.</summary>
    public byte[] Picture { get; private set; } = [];

    /// <summary>Runs one frame and draws it into <see cref="Picture"/>.</summary>
    public void Frame(Action draw)
    {
        ImGui.NewFrame();
        draw();
        ImGui.Render();
        var data = ImGui.GetDrawData();
        Vertices = data.TotalVtxCount;
        Picture = Rasterize(data);
    }

    /// <summary>Presses and lets go a key over two frames, as a player's tap arrives.</summary>
    public void Tap(ImGuiKey key, Action draw)
    {
        ImGui.GetIO().AddKeyEvent(key, true);
        Frame(draw);
        ImGui.GetIO().AddKeyEvent(key, false);
        Frame(draw);
    }

    /// <summary>Types a text into the field that has the keyboard, then runs a frame.</summary>
    public void Type(string text, Action draw)
    {
        ImGui.GetIO().AddInputCharactersUTF8(text);
        Frame(draw);
    }

    /// <summary>How many of the picture's pixels are not the white ground.</summary>
    public int Drawn()
    {
        var drawn = 0;
        for (var i = 0; i < Picture.Length; i += 4)
            if (Picture[i] != 255 || Picture[i + 1] != 255 || Picture[i + 2] != 255) drawn++;
        return drawn;
    }

    public void Dispose() => ImGui.DestroyContext(_context);

    private byte[] Rasterize(ImDrawDataPtr data)
    {
        var picture = new byte[Width * Height * 4];
        Array.Fill(picture, (byte)255);
        for (var n = 0; n < data.CmdListsCount; n++)
        {
            var list = data.CmdLists[n];
            var vertices = new byte[list.VtxBuffer.Size * 20];
            if (vertices.Length > 0) Marshal.Copy(list.VtxBuffer.Data, vertices, 0, vertices.Length);
            var indices = new short[list.IdxBuffer.Size];
            if (indices.Length > 0) Marshal.Copy(list.IdxBuffer.Data, indices, 0, indices.Length);
            var corners = MemoryMarshal.Cast<byte, Corner>(vertices);

            for (var c = 0; c < list.CmdBuffer.Size; c++)
            {
                var command = list.CmdBuffer[c];
                var clip = command.ClipRect;
                for (var i = 0; i + 2 < (int)command.ElemCount; i += 3)
                {
                    var at = (int)command.IdxOffset + i;
                    var offset = (int)command.VtxOffset;
                    Triangle(picture, clip,
                        corners[offset + (ushort)indices[at]], corners[offset + (ushort)indices[at + 1]], corners[offset + (ushort)indices[at + 2]]);
                }
            }
        }

        return picture;
    }

    // One triangle, its color and its texel taken at each pixel's center and laid over the picture
    // by the color's alpha, as a GPU's blend of source alpha over the frame does.
    private void Triangle(byte[] picture, Vector4 clip, Corner a, Corner b, Corner c)
    {
        var area = Edge(a.Position, b.Position, c.Position);
        if (area == 0) return;
        var left = Math.Max((int)MathF.Floor(MathF.Min(a.Position.X, MathF.Min(b.Position.X, c.Position.X))), Math.Max(0, (int)clip.X));
        var right = Math.Min((int)MathF.Ceiling(MathF.Max(a.Position.X, MathF.Max(b.Position.X, c.Position.X))), Math.Min(Width, (int)clip.Z));
        var top = Math.Max((int)MathF.Floor(MathF.Min(a.Position.Y, MathF.Min(b.Position.Y, c.Position.Y))), Math.Max(0, (int)clip.Y));
        var bottom = Math.Min((int)MathF.Ceiling(MathF.Max(a.Position.Y, MathF.Max(b.Position.Y, c.Position.Y))), Math.Min(Height, (int)clip.W));
        for (var y = top; y < bottom; y++)
        for (var x = left; x < right; x++)
        {
            var point = new Vector2(x + 0.5f, y + 0.5f);
            var (wa, wb, wc) = (Edge(b.Position, c.Position, point) / area, Edge(c.Position, a.Position, point) / area, Edge(a.Position, b.Position, point) / area);
            if (wa < 0 || wb < 0 || wc < 0) continue;

            var uv = a.Uv * wa + b.Uv * wb + c.Uv * wc;
            var texel = (Math.Clamp((int)(uv.Y * _atlasHeight), 0, _atlasHeight - 1) * _atlasWidth + Math.Clamp((int)(uv.X * _atlasWidth), 0, _atlasWidth - 1)) * 4;
            var pixel = (y * Width + x) * 4;
            var alpha = (Channel(a, 3) * wa + Channel(b, 3) * wb + Channel(c, 3) * wc) / 255f * (_atlas[texel + 3] / 255f);
            for (var channel = 0; channel < 3; channel++)
            {
                var color = (Channel(a, channel) * wa + Channel(b, channel) * wb + Channel(c, channel) * wc) * (_atlas[texel + channel] / 255f);
                picture[pixel + channel] = (byte)Math.Clamp(MathF.Round(color * alpha + picture[pixel + channel] * (1 - alpha)), 0, 255);
            }
        }
    }

    private static float Channel(Corner corner, int channel) => (corner.Color >> (8 * channel)) & 255;

    private static float Edge(Vector2 a, Vector2 b, Vector2 p) => (b.X - a.X) * (p.Y - a.Y) - (b.Y - a.Y) * (p.X - a.X);

    // ImGui's vertex, 20 bytes, a position, a texture coordinate and a color whose lowest byte is
    // red.
    [StructLayout(LayoutKind.Sequential)]
    private readonly struct Corner
    {
        public readonly Vector2 Position;
        public readonly Vector2 Uv;
        public readonly uint Color;
    }

    /// <summary>Writes an RGBA picture as a PNG file.</summary>
    public static void WritePng(string path, byte[] rgba, int width, int height)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var file = File.Create(path);
        file.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);

        var header = new byte[13];
        BigEndian(header, 0, width);
        BigEndian(header, 4, height);
        (header[8], header[9]) = (8, 6);
        Chunk(file, "IHDR", header);

        using var packed = new MemoryStream();
        using (var zlib = new ZLibStream(packed, CompressionLevel.Optimal, leaveOpen: true))
            for (var y = 0; y < height; y++)
            {
                // Each row begins with its filter, none.
                zlib.WriteByte(0);
                zlib.Write(rgba, y * width * 4, width * 4);
            }

        Chunk(file, "IDAT", packed.ToArray());
        Chunk(file, "IEND", []);
    }

    private static void Chunk(Stream file, string name, byte[] data)
    {
        var length = new byte[4];
        BigEndian(length, 0, data.Length);
        file.Write(length);
        var named = System.Text.Encoding.ASCII.GetBytes(name).Concat(data).ToArray();
        file.Write(named);
        var crc = 0xFFFFFFFFu;
        foreach (var value in named)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++) crc = (crc & 1) != 0 ? 0xEDB88320u ^ (crc >> 1) : crc >> 1;
        }

        var check = new byte[4];
        BigEndian(check, 0, (int)~crc);
        file.Write(check);
    }

    private static void BigEndian(byte[] into, int at, int value)
    {
        (into[at], into[at + 1], into[at + 2], into[at + 3]) = ((byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value);
    }
}
