using System.Diagnostics;
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

    private const string FilePath = "C:\\Users\\matya\\Downloads\\video.hevc";

    static void Main(string[] args) {
        File.Create(FilePath).Close();
        
        using UdpClient ravenSocket = new();
        ravenSocket.ExclusiveAddressUse = false;
        ravenSocket.Connect(IPEndPoint.Parse("192.168.2.1:9005"));
        
        using UdpClient videoSocket = new();
        videoSocket.ExclusiveAddressUse = false;
        videoSocket.Client.Bind(new IPEndPoint(IPAddress.Loopback, 8887));
        videoSocket.Connect(IPAddress.Loopback, 8888);

        ravenSocket.Send(Handshake);
        Console.WriteLine("Handshake: Sent");
        
        List<byte> currentFrame = [];

        Process ffplay = new Process();
        ffplay.StartInfo.FileName = "ffplay.exe";
        ffplay.StartInfo.Arguments = "-i -";
        ffplay.StartInfo.UseShellExecute = false;
        ffplay.StartInfo.RedirectStandardInput = true;
        ffplay.StartInfo.RedirectStandardOutput = true;

        ffplay.Start();

        while (true) {
            Console.WriteLine("-----------------------------------------------------------------");
            
            IPEndPoint? ipep = null;
            byte[] receivedData = ravenSocket.Receive(ref ipep);
            int packetLength = readShortAtOffset(receivedData, 0) & 0x7fff;
            Console.WriteLine($"Packet: Length: {packetLength}");
            if (receivedData.Length != packetLength) {
                Console.WriteLine(
                    $"[WARN] packet size mismatch, continuing (debug only), (raven: {packetLength})/(packet:{receivedData.Length})");
            }

            ushort sequenceNumber = readShortAtOffset(receivedData, 4);
            Console.WriteLine($"Packet: Sequence Number: {sequenceNumber >> 3}");

            byte packetType = receivedData[6];
            Console.WriteLine($"Packet: Type: {packetType}");

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

                    int recvWindowStart = sequenceNumber;
                    int recvWindowEnd = sequenceNumber;
                    
                    Console.WriteLine($"Frame: Received #{frameNumber} ({partNumber + 1}/{totalParts})");

                    byte[] h265 = new byte[packetLength - 0x14];
                    Buffer.BlockCopy(receivedData, 0x14, h265, 0, packetLength - 0x14);
                    
                    currentFrame.AddRange(h265);
                    
                    // When the last part of a frame is received
                    if (partNumber == totalParts - 1) {
                        Console.WriteLine($"Frame: Finished #{frameNumber} ({currentFrame.Count} bytes)");
                        
                        byte[] keepalive = (byte[])KeepAlive.Clone();
                        keepalive[0x08] = (byte)(recvWindowStart & 0xff);
                        keepalive[0x09] = (byte)(recvWindowStart >> 8);
                        keepalive[0x0a] = (byte)(recvWindowEnd & 0xff);
                        keepalive[0x0b] = (byte)(recvWindowEnd >> 8);

                        ravenSocket.Send(keepalive);

                        byte[] completeFrame = new byte[currentFrame.Count];
                        currentFrame.CopyTo(completeFrame);
                        currentFrame.Clear();
                        
                        // Send it to the UDP stream
                        videoSocket.Send(completeFrame, completeFrame.Length);
                        
                        // Append it to the file
                        using Stream s = File.Open(FilePath, FileMode.Append);
                        s.Write(completeFrame);

                        using var writer = new BinaryWriter(ffplay.StandardInput.BaseStream) ;
                        writer.Write(completeFrame);
                    }
                    break;
                default:
                    Console.WriteLine($"Unknown packet type {packetType}");
                    break;
            }
        }
    }

    private static ushort readShortAtOffset(byte[] bytes, int i, bool inverted = false) {
        return !inverted ? (ushort)(bytes[i] | (bytes[i + 1] << 8)) : (ushort)(bytes[i + 1] | bytes[i] << 8);
    }
}