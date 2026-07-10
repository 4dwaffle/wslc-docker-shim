using System.Buffers.Binary;
using System.Text;

namespace Testcontainers.WslcShim.Docker;

public static class DockerRawStream
{
    public static byte[] FromStdout(string text)
    {
        var payload = Encoding.UTF8.GetBytes(text);
        var frame = new byte[8 + payload.Length];
        frame[0] = 1;
        BinaryPrimitives.WriteUInt32BigEndian(frame.AsSpan(4, 4), (uint)payload.Length);
        payload.CopyTo(frame.AsSpan(8));
        return frame;
    }

}
