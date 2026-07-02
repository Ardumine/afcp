using System.Security.Cryptography;
using System.Text;

namespace AFCP;

/// <summary>
/// Per-message confidentiality decorator over an <see cref="IMessageStream"/>.
/// During <see cref="Initialize"/>, the two peers run an ephemeral ECDH
/// (nistP256) key exchange — each sends its public key as one framed message
/// (the underlying <see cref="Framing"/>/<see cref="Checksum"/> stack delivers
/// it whole) — derive a shared secret via HKDF-SHA256, and configure AES-CFB
/// (feedback size 8). Each subsequent message is encrypted/decrypted as a unit.
///
/// The handshake rides the message layer, so it is itself framed and (if stacked
/// below) checksummed — but it is NOT checksummed itself if <see cref="Checksum"/>
/// sits above. Recommended stack order:
/// <c>Framing → Checksum → Crypto</c> (checksum the plaintext, then encrypt;
/// the ciphertext is what crosses the wire).
/// </summary>
public sealed class Crypto : IMessageStream
{
    private readonly IMessageStream _base;
    private Aes? _aes;
    private ICryptoTransform? _encryptor;
    private ICryptoTransform? _decryptor;

    public Crypto(IMessageStream baseStream) => _base = baseStream;

    public bool IsConnected => _base.IsConnected;
    public event Action? OnDisconnect { add => _base.OnDisconnect += value; remove => _base.OnDisconnect -= value; }

    public IMessageStream Initialize(bool isServer)
    {
        // Propagate init first (lower layers' handshake), then run ours on top.
        _base.Initialize(isServer);

        using var localECDH = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        var localPublicKey = localECDH.ExportSubjectPublicKeyInfo();

        // Exchange public keys as framed messages.
        if (isServer)
        {
            var peerKey = _base.Read().ToArray();
            _base.Write(localPublicKey);
            EstablishCrypto(localECDH, peerKey);
        }
        else
        {
            _base.Write(localPublicKey);
            var peerKey = _base.Read().ToArray();
            EstablishCrypto(localECDH, peerKey);
        }
        return this;
    }

    private void EstablishCrypto(ECDiffieHellman localECDH, byte[] peerPublicKey)
    {
        using var peerECDH = ECDiffieHellman.Create();
        peerECDH.ImportSubjectPublicKeyInfo(peerPublicKey, out _);
        var sharedSecret = localECDH.DeriveKeyMaterial(peerECDH.PublicKey);

        var keyMaterial = HKDF.DeriveKey(
            HashAlgorithmName.SHA256, sharedSecret, outputLength: 48,
            salt: null, info: Encoding.UTF8.GetBytes("AFCP Crypto Transformer"));

        _aes = Aes.Create();
        _aes.Key = keyMaterial.AsSpan(0, 32).ToArray();
        _aes.IV = keyMaterial.AsSpan(32, 16).ToArray();
        _aes.Mode = CipherMode.CFB;
        _aes.Padding = PaddingMode.None;
        _aes.FeedbackSize = 8;
        _encryptor = _aes.CreateEncryptor();
        _decryptor = _aes.CreateDecryptor();
    }

    public void Write(ReadOnlySpan<byte> message)
    {
        if (_encryptor == null) throw new InvalidOperationException("Crypto not initialized.");
        var cipher = _encryptor.TransformFinalBlock(message.ToArray(), 0, message.Length);
        _base.Write(cipher);
    }

    public ReadOnlySpan<byte> Read()
    {
        if (_decryptor == null) throw new InvalidOperationException("Crypto not initialized.");
        var cipher = _base.Read();
        if (cipher.Length == 0) return cipher;
        return _decryptor.TransformFinalBlock(cipher.ToArray(), 0, cipher.Length);
    }

    public void Dispose()
    {
        _encryptor?.Dispose();
        _decryptor?.Dispose();
        _aes?.Dispose();
        _base.Dispose();
    }
}
