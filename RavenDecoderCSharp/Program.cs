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

	private static List<byte> Buf = [];
	
	static void Main(string[] args) {
		using UdpClient ravenSocket = new(8889);
		using UdpClient videoSocket = new(8887);

		ravenSocket.Send(Handshake,IPEndPoint.Parse("192.168.2.1:9005"));
		Console.WriteLine("sent handshake");

		while (true) {
			IPEndPoint? ipep = null;
			byte[] receivedData = ravenSocket.Receive(ref ipep);
			Console.WriteLine("-----------------------------------------------------------------");
			int length = readShortAtOffset(receivedData, 0) & 0x7fff;
			Console.WriteLine($"length {length}");
			if (receivedData.Length != length) {
				Console.WriteLine($"[WARN] packet size mismatch, continuing (debug only), (raven: {length})/(packet:{receivedData.Length})");
			}

			short seq_no = readShortAtOffset(receivedData, 4);
			Console.WriteLine($"seq_no: {seq_no}");
			
			byte packetType = receivedData[6];
			Console.WriteLine($"packetType: {packetType}");

			switch (packetType) {
				case 0:
					Console.WriteLine("Got handshake response");
					break;
				case 1:
				case 3:
					Console.WriteLine("Got telemetry");
					break;
				case 2:
					byte frame_no = receivedData[0x10];
					int n_parts = receivedData[0x11] & 0x7f;
					int part_num = (receivedData[0x11] >> 7) + ((receivedData[0x12] & 0x1f) << 1);
					
					Console.WriteLine($"Got video frame {frame_no} ({part_num+1}/{n_parts})");

					byte[] h265 = new byte[length-0x14];
					Buffer.BlockCopy(receivedData,0x14,h265,0,length-0x14);
					
					int recv_window_start = seq_no;
					int recv_window_end = seq_no;
					Stream s = File.Open("./video.hevc", FileMode.Append);
					if (part_num == n_parts - 1) {
						byte[] keepalive = (byte[])KeepAlive.Clone();
						keepalive[0x08] = (byte) (recv_window_start & 0xff);
						keepalive[0x09] = (byte) (recv_window_start >> 8);
						keepalive[0x0a] = (byte) (recv_window_end & 0xff);
						keepalive[0x0b] = (byte) (recv_window_end >> 8);

						ravenSocket.Send(keepalive,IPEndPoint.Parse("192.168.2.1:9005"));

						videoSocket.Send(Buf.ToArray(), Buf.Count, IPEndPoint.Parse("127.0.0.1:8888"));
						Buf.Clear();
					}
					else {
						Buf.AddRange(h265);
					}

					s.Write(h265);
					s.Close();
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