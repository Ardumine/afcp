using System.Security.Cryptography;
using System.Text;
using AFCP.Core;
using AFCP.Core.Implementations;
using AFCP.Core.Transformers;
using AFCP.Core.Utils;



internal class Program
{

    static byte[] Encrypt(Span<byte> data, byte[] Key, byte[] IV)
    {
        byte[] encrypted;
        // Create a new AesManaged.
        using (var aes = Aes.Create())
        {
            aes.Padding = PaddingMode.PKCS7;
            aes.Key = Key;
            aes.IV = IV;
            // Create encryptor
            ICryptoTransform encryptor = aes.CreateEncryptor(Key, IV);
            // Create MemoryStream
            using (MemoryStream ms = new MemoryStream())
            {
                // Create crypto stream using the CryptoStream class. This class is the key to encryption
                // and encrypts and decrypts data from any given stream. In this case, we will pass a memory stream
                // to encrypt
                using (CryptoStream cs = new CryptoStream(ms, encryptor, CryptoStreamMode.Write))
                {
                    // Create StreamWriter and write data to a stream
                    cs.Write(data);
                    cs.FlushFinalBlock();

                    encrypted = ms.ToArray();
                }
            }
        }
        // Return encrypted data
        return encrypted;
    }

    static byte[] Decrypt(ReadOnlySpan<byte> cipherText, byte[] Key, byte[] IV)
    {
        byte[] decrypted;

        // Create AesManaged
        using (var aes = Aes.Create())
        {
            aes.Padding = PaddingMode.PKCS7;

            aes.Key = Key;
            aes.IV = IV;
            // Create a decryptor
            ICryptoTransform decryptor = aes.CreateDecryptor(Key, IV);
            // Create the streams used for decryption.
            using (MemoryStream ms = new MemoryStream())
            {
                // Create crypto stream
                using (CryptoStream cs = new CryptoStream(ms, decryptor, CryptoStreamMode.Write))
                {
                    // Read crypto stream
                    cs.Write(cipherText);
                    cs.FlushFinalBlock();

                    decrypted = ms.ToArray();
                }
            }
        }
        return decrypted;
    }
    private static void Maine(string[] args)
    {
        var aes = Aes.Create();
        aes.Padding = PaddingMode.PKCS7;
        aes.GenerateIV(); // Gera um IV aleatório
        aes.GenerateKey();
        var _AesIv = aes.IV;
        var _AesKey = aes.Key;
        var encrypted = Encrypt(new byte[100], _AesKey, _AesIv);

        var decrypted = Decrypt(encrypted, _AesKey, _AesIv);

        Console.WriteLine($"Encrypted: {BitConverter.ToString(encrypted)}");
        Console.WriteLine($"Decrypted: {BitConverter.ToString(decrypted)}");

    }
    private static void Main(string[] args)
    {
        var (streamClient4Server, streamServer4Client) = StreamUtils.CreateBidirectionalStreams();

        Streamy pipeClient = new StreamyFromStream(streamClient4Server);
        pipeClient = new HTTPTranformer(pipeClient);
        pipeClient = new DataCompletionTransformer(pipeClient);
        pipeClient = new EncryptionTransformer((DataCompletionTransformer)pipeClient);
        pipeClient = new Logger(pipeClient, "Client");

        new Thread(() =>
        {
            Streamy pipeServer = new StreamyFromStream(streamServer4Client);
            pipeServer = new HTTPTranformer(pipeServer);
            pipeServer = new DataCompletionTransformer(pipeServer);
            pipeServer = new EncryptionTransformer((DataCompletionTransformer)pipeServer);
            pipeServer = new Logger(pipeServer, "Server");

            pipeServer.Initialize(new StreamyParameters()
            {
                IsServer = true
            });

            //Simulate communication
            pipeServer.Write("Ok Garmin"u8);
            Console.WriteLine($"Read from client: {Encoding.UTF8.GetString(pipeServer.ReadAll())}");

            Console.WriteLine("Server code executed successfully.");
        }).Start();

        pipeClient.Initialize(new StreamyParameters()
        {
            IsServer = false
        });

        Console.WriteLine("---------------------------------------------------------------------------------");


        Console.WriteLine($"Read from server: {Encoding.UTF8.GetString(pipeClient.ReadAll())}");
        pipeClient.Write("Video speichern!"u8);

        Console.WriteLine("Done!");
    }


}
