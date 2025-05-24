using System.Net;
using System.Net.Sockets;

namespace RavenDecoderCSharp;

class Program {
	private static readonly byte[] Handshake = [
		0x30, 0x80, 0x3a, 0xdd, 0x00, 0x00, 0x00, 0x57, 0xd0, 0xe9, 0x64, 0x00, 0x64, 0x00, 0xc0, 0x05,
		0x14, 0x00, 0x00, 0x0a, 0x00, 0x64, 0x00, 0x64, 0x00, 0xc0, 0x05, 0x14, 0x00, 0x00, 0x64, 0x00,
		0x14, 0x00, 0x64, 0x00, 0xc0, 0x05, 0x14, 0x00, 0x00, 0x64, 0x00, 0x01, 0x01, 0x04, 0x0a, 0x02
	];

	private static readonly byte[] KeepAlive = [
		0x1e, 0x80, 0x3a, 0xdd, 0x00, 0x00, 0x04, 0x7d, 0x08, 0xea, 0x08, 0xea, 0x00, 0x00, 0xd0, 0xe9,
		0xd0, 0xe9, 0x00, 0x00, 0xd0, 0xe9, 0xd8, 0xe9, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00
	];

	private static List<byte> _buf = [];
	
	private const string FilePath = "C:\\Users\\matya\\Downloads\\video.hevc";
	
	static void Main(string[] args) {
		using UdpClient ravenSocket = new(8889);
		using UdpClient videoSocket = new(8887);

		ravenSocket.Send(Handshake,IPEndPoint.Parse("192.168.2.1:9005"));
		Console.WriteLine("Handshake: Sent");

		File.Create(FilePath).Close();

		while (true) {
			IPEndPoint? ipep = null;
			byte[] receivedData = ravenSocket.Receive(ref ipep);
			Console.WriteLine("-----------------------------------------------------------------");
			int length = readShortAtOffset(receivedData, 0) & 0x7fff;
			Console.WriteLine($"length {length}");
			if (receivedData.Length != length) {
				Console.WriteLine($"[WARN] packet size mismatch, continuing (debug only), (raven: {length})/(packet:{receivedData.Length})");
			}

			short sequenceNumber = readShortAtOffset(receivedData, 4);
			Console.WriteLine($"seq_no: {sequenceNumber}");
			
			byte packetType = receivedData[6];
			Console.WriteLine($"packetType: {packetType}");

			switch (packetType) {
				case 0:
					Console.WriteLine("Handshake: Response received");
					break;
				case 1:
				case 3:
					Console.WriteLine("Telemetry: Received");
					break;
				case 2:
					byte frameNumber = receivedData[0x10];
					int totalParts = receivedData[0x11] & 0x7f;
					int partNumber = (receivedData[0x11] >> 7) + ((receivedData[0x12] & 0x1f) << 1);
					
					Console.WriteLine($"Frame: Received #{frameNumber} ({partNumber+1}/{totalParts})");

					byte[] h265 = new byte[length-0x14];
					Buffer.BlockCopy(receivedData,0x14,h265,0,length-0x14);
					
					int recvWindowStart = sequenceNumber;
					int recvWindowEnd = sequenceNumber;
					
					using (Stream s = File.Open(FilePath, FileMode.Append)) {
						if (partNumber == totalParts - 1) {
							byte[] keepalive = (byte[])KeepAlive.Clone();
							keepalive[0x08] = (byte) (recvWindowStart & 0xff);
							keepalive[0x09] = (byte) (recvWindowStart >> 8);
							keepalive[0x0a] = (byte) (recvWindowEnd & 0xff);
							keepalive[0x0b] = (byte) (recvWindowEnd >> 8);

							ravenSocket.Send(keepalive,IPEndPoint.Parse("192.168.2.1:9005"));

							byte[] framebuf = new byte[_buf.Count];
							_buf.CopyTo(framebuf);
							videoSocket.Send(framebuf, framebuf.Length, IPEndPoint.Parse("127.0.0.1:8888"));
							_buf.Clear();
						}
						else {
							_buf.AddRange(h265);
						}
						
						s.Write(h265);
					}
					break;	
				default:
					Console.WriteLine($"Unknown packet type {packetType}");
					break;
			}
		}
	}

	private static short readShortAtOffset(byte[] bytes, int i) {
		return (short)(bytes[i] | (bytes[i+1] << 8));
	}
}