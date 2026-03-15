using System;
using System.IO;
using System.Text;
class Program {
  static int Read32BE(byte[] b, int o) {
    if (o + 4 > b.Length) return 0;
    return (b[o] << 24) | (b[o+1] << 16) | (b[o+2] << 8) | b[o+3];
  }
  static int Read16BE(byte[] b, int o) {
    if (o + 2 > b.Length) return 0;
    return (b[o] << 8) | b[o+1];
  }
  static void Main() {
    string path = @"Assets\_gm\Features\Paint\Editor\TestAbr\Resource Boy - Stipple Brushes.abr";
    byte[] data = File.ReadAllBytes(path);
    Console.WriteLine("File size: " + data.Length);
    int v1 = Read16BE(data, 0); int v2 = Read16BE(data, 2);
    Console.WriteLine("Version: " + v1 + "." + v2);
    int pos = 4;
    while (pos + 16 <= data.Length) {
      if (data[pos]!=0x38||data[pos+1]!=0x42||data[pos+2]!=0x49||data[pos+3]!=0x4D) { pos++; continue; }
      string type = Encoding.ASCII.GetString(data, pos+8, 4);
      int size = Read32BE(data, pos+12);
      Console.WriteLine("8BIM at " + pos + " type='" + type + "' size=" + size);
      if (type == "samp") {
        int start = pos + 16;
        int len = size;
        int first4 = Read32BE(data, start);
        Console.WriteLine("  SAMP payload: start=" + start + " first4(BE)=" + first4);
        int n = data[start+4];
        if (n >= 0 && n <= 200 && start+5+n+8+21 <= start+len) {
          int hdr = start+4+1+n+8;
          int depth = Read16BE(data, hdr);
          int top = Read32BE(data, hdr+2), left = Read32BE(data, hdr+6);
          int bottom = Read32BE(data, hdr+10), right = Read32BE(data, hdr+14);
          int d2 = Read16BE(data, hdr+18); int comp = data[hdr+20];
          Console.WriteLine("  Just Solve at +4: Pascal n=" + n + " 21-byte hdr at " + hdr + " depth=" + depth + " top=" + top + " left=" + left + " bottom=" + bottom + " right=" + right + " w=" + (right-left) + " h=" + (bottom-top) + " comp=" + comp);
        }
        break;
      }
      pos = pos + 16 + size;
      if (pos % 2 != 0) pos++;
    }
  }
}
