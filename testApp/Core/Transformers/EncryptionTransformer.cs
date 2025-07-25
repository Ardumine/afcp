using System;
using System.Security.Cryptography;
using System.Text;

namespace AFCP.Core.Transformers
{
    public class EncryptionTransformer : ApresentationTransformer, IDisposable
    {
        private Aes? _aes;
        private ICryptoTransform? _encryptor;
        private ICryptoTransform? _decryptor;

        public EncryptionTransformer(Streamy baseStream) : base(baseStream)
        {
        }


        public override ApresentationTransformer Initialize(StreamyParameters parameters)
        {
            // Generate ephemeral ECDH keys
            using ECDiffieHellman localECDH = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
            byte[] localPublicKey = localECDH.ExportSubjectPublicKeyInfo();

            // Use the DataCompletionTransformer to handle the key exchange
            // so we don't lose any data
            var dataCompletionTransformer = new DataCompletionTransformer(_baseStream);

            // Exchange public keys
            if (parameters.IsServer)
            {
                // Server: receive client public key first
                byte[] peerPublicKey = dataCompletionTransformer.ReadAll().ToArray();
                dataCompletionTransformer.Write(localPublicKey);
                EstablishCrypto(localECDH, peerPublicKey);
            }
            else
            {
                // Client: send public key first
                dataCompletionTransformer.Write(localPublicKey);
                byte[] peerPublicKey = dataCompletionTransformer.ReadAll().ToArray();
                EstablishCrypto(localECDH, peerPublicKey);
            }

            _baseStream.Initialize(parameters);
            return this;
        }

        private void EstablishCrypto(ECDiffieHellman localECDH, byte[] peerPublicKey)
        {
            // Import peer public key
            using ECDiffieHellman peerECDH = ECDiffieHellman.Create();
            peerECDH.ImportSubjectPublicKeyInfo(peerPublicKey, out _);

            // Derive shared secret
            byte[] sharedSecret = localECDH.DeriveKeyMaterial(peerECDH.PublicKey);

            // Derive AES key and IV using HKDF
            byte[] keyMaterial = HKDF.DeriveKey(
                HashAlgorithmName.SHA256,
                sharedSecret,
                outputLength: 48,
                salt: null,
                info: Encoding.UTF8.GetBytes("AFCP Encryption Transformer")
            );

            byte[] key = keyMaterial.AsSpan(0, 32).ToArray();
            byte[] iv = keyMaterial.AsSpan(32, 16).ToArray();

            // Configure AES in CFB mode
            _aes = Aes.Create();
            _aes.Key = key;
            _aes.IV = iv;
            _aes.Mode = CipherMode.CFB;
            _aes.Padding = PaddingMode.None;
            _aes.FeedbackSize = 8;

            _encryptor = _aes.CreateEncryptor();
            _decryptor = _aes.CreateDecryptor();
        }

        public override int Read(Span<byte> buffer)
        {
            int bytesRead = _baseStream.Read(buffer);
            if (bytesRead > 0)
            {
                byte[] ciphertext = buffer.Slice(0, bytesRead).ToArray();
                var plaintext = _decryptor?.TransformFinalBlock(ciphertext, 0, ciphertext.Length);
                plaintext.CopyTo(buffer);
            }
            return bytesRead;
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            var encrypted = _encryptor?.TransformFinalBlock(buffer.ToArray(), 0, buffer.Length);
            _baseStream.Write(encrypted);
        }

        public override ReadOnlySpan<byte> ReadAll()
        {
            byte[] encrypted = _baseStream.ReadAll().ToArray();
            return _decryptor?.TransformFinalBlock(encrypted, 0, encrypted.Length);
        }

        public void Dispose()
        {
            _encryptor?.Dispose();
            _decryptor?.Dispose();
            _aes?.Dispose();
        }
    }
}