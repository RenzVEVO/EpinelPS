using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using EpinelPS.Data;
using Newtonsoft.Json;

namespace EpinelPS.Utils;

public static class StaticDataPatcher
{
    private class GameCommon
    {
        public byte[] SmallNumber { get; set; } = [];
    }

    /// <summary>
    /// Checks if StaticData.pack contains unlocalized video cutscene links
    /// and patches ScenarioMovieTable.mpk to point to working localized CDN videos.
    /// </summary>
    public static bool TryPatch(string packPath, StaticData data)
    {
        if (!File.Exists(packPath))
        {
            return false;
        }

        try
        {
            string commonPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "gamecommon.json");
            if (!File.Exists(commonPath))
            {
                Logging.WriteLine("[StaticDataPatcher] gamecommon.json not found; skipping patch check.", LogType.Warning);
                return false;
            }

            var common = JsonConvert.DeserializeObject<GameCommon>(File.ReadAllText(commonPath));
            if (common == null || common.SmallNumber.Length == 0)
            {
                return false;
            }

            byte[] fileBytes = File.ReadAllBytes(packPath);

            // Step 1: Derive key2 for outer AES-CBC layer
            byte[] key2 = Rfc2898DeriveBytes.Pbkdf2(
                common.SmallNumber,
                data.GetSalt2Bytes(),
                10000,
                HashAlgorithmName.SHA256,
                32);

            byte[] decryptedOuter;
            using (Aes aesOuter = Aes.Create())
            {
                aesOuter.Key = key2[..16];
                aesOuter.IV = key2[16..];
                aesOuter.Mode = CipherMode.CBC;
                aesOuter.Padding = PaddingMode.PKCS7;

                using MemoryStream msIn = new(fileBytes);
                using CryptoStream decryptStream = new(msIn, aesOuter.CreateDecryptor(), CryptoStreamMode.Read);
                using MemoryStream msOut = new();
                decryptStream.CopyTo(msOut);
                decryptedOuter = msOut.ToArray();
            }

            // Step 2: Read outer zip to get "sign" and "data"
            byte[] signBytes;
            byte[] dataBytes;
            using (MemoryStream outerZipMs = new(decryptedOuter))
            using (ZipArchive outerZip = new(outerZipMs, ZipArchiveMode.Read))
            {
                ZipArchiveEntry? signEntry = outerZip.GetEntry("sign");
                ZipArchiveEntry? dataEntry = outerZip.GetEntry("data");
                if (signEntry == null || dataEntry == null)
                {
                    return false;
                }

                using MemoryStream signMs = new();
                using (Stream s = signEntry.Open()) s.CopyTo(signMs);
                signBytes = signMs.ToArray();

                using MemoryStream dataMs = new();
                using (Stream s = dataEntry.Open()) s.CopyTo(dataMs);
                dataBytes = dataMs.ToArray();
            }

            // Step 3: Decrypt inner data using AES-CTR (key3)
            byte[] key3 = Rfc2898DeriveBytes.Pbkdf2(
                common.SmallNumber,
                data.GetSalt1Bytes(),
                10000,
                HashAlgorithmName.SHA256,
                32);

            byte[] innerZipBytes;
            using (MemoryStream dataInMs = new(dataBytes))
            using (MemoryStream innerZipOutMs = new())
            {
                GameData.DoTransformation(key3[..16], key3[16..], dataInMs, innerZipOutMs);
                innerZipBytes = innerZipOutMs.ToArray();
            }

            // Step 4: Check if ScenarioMovieTable.mpk needs patching
            byte[] oldLink = MakeMemoryPackString("https://cloud.nikke-kr.com/media/coinrushshowdown/coinrushshowdown.mp4");
            bool needsPatch = false;

            using (MemoryStream innerZipMs = new(innerZipBytes))
            using (ZipArchive innerZip = new(innerZipMs, ZipArchiveMode.Read))
            {
                ZipArchiveEntry? movieEntry = innerZip.GetEntry("ScenarioMovieTable.mpk");
                if (movieEntry == null)
                {
                    return false;
                }

                using MemoryStream movieMs = new();
                using (Stream s = movieEntry.Open()) s.CopyTo(movieMs);
                byte[] movieBytes = movieMs.ToArray();

                if (IndexOf(movieBytes, oldLink) != -1)
                {
                    needsPatch = true;
                }
            }

            if (!needsPatch)
            {
                return false;
            }

            Logging.WriteLine("[StaticDataPatcher] Unlocalized coinrushshowdown cutscene URLs detected in StaticData.pack. Applying fix...", LogType.Info);

            // Step 5: Repack inner zip with patched ScenarioMovieTable.mpk
            byte[] newInnerZipBytes;
            using (MemoryStream innerZipMs = new(innerZipBytes))
            using (ZipArchive innerZip = new(innerZipMs, ZipArchiveMode.Read))
            using (MemoryStream newInnerMs = new())
            {
                using (ZipArchive newInnerZip = new(newInnerMs, ZipArchiveMode.Create, leaveOpen: true))
                {
                    foreach (ZipArchiveEntry entry in innerZip.Entries)
                    {
                        ZipArchiveEntry newEntry = newInnerZip.CreateEntry(entry.FullName, CompressionLevel.Optimal);
                        using Stream src = entry.Open();
                        using Stream dst = newEntry.Open();

                        if (entry.FullName == "ScenarioMovieTable.mpk")
                        {
                            using MemoryStream movieMs = new();
                            src.CopyTo(movieMs);
                            byte[] patchedMovie = PatchScenarioMovieTable(movieMs.ToArray(), oldLink);
                            dst.Write(patchedMovie, 0, patchedMovie.Length);
                        }
                        else
                        {
                            src.CopyTo(dst);
                        }
                    }
                }
                newInnerZipBytes = newInnerMs.ToArray();
            }

            // Step 6: Encrypt inner zip with AES-CTR
            byte[] newEncryptedData;
            using (MemoryStream newInnerStream = new(newInnerZipBytes))
            using (MemoryStream encryptedDataMs = new())
            {
                GameData.DoTransformation(key3[..16], key3[16..], newInnerStream, encryptedDataMs);
                newEncryptedData = encryptedDataMs.ToArray();
            }

            // Step 7: Build new outer zip (uncompressed) containing original sign and new data
            byte[] newOuterZipBytes;
            using (MemoryStream newOuterMs = new())
            {
                using (ZipArchive newOuterZip = new(newOuterMs, ZipArchiveMode.Create, leaveOpen: true))
                {
                    ZipArchiveEntry newSignEntry = newOuterZip.CreateEntry("sign", CompressionLevel.NoCompression);
                    using (Stream dst = newSignEntry.Open())
                    {
                        dst.Write(signBytes, 0, signBytes.Length);
                    }

                    ZipArchiveEntry newDataEntry = newOuterZip.CreateEntry("data", CompressionLevel.NoCompression);
                    using (Stream dst = newDataEntry.Open())
                    {
                        dst.Write(newEncryptedData, 0, newEncryptedData.Length);
                    }
                }
                newOuterZipBytes = newOuterMs.ToArray();
            }

            // Step 8: Encrypt outer zip using AES-CBC
            byte[] newPackBytes;
            using (Aes aesOuter = Aes.Create())
            {
                aesOuter.Key = key2[..16];
                aesOuter.IV = key2[16..];
                aesOuter.Mode = CipherMode.CBC;
                aesOuter.Padding = PaddingMode.PKCS7;

                using MemoryStream msIn = new(newOuterZipBytes);
                using MemoryStream msOut = new();
                using (CryptoStream encryptStream = new(msOut, aesOuter.CreateEncryptor(), CryptoStreamMode.Write))
                {
                    msIn.CopyTo(encryptStream);
                    encryptStream.FlushFinalBlock();
                }
                newPackBytes = msOut.ToArray();
            }

            // Step 9: Backup original and write patched file
            string backupPath = packPath + ".bak";
            if (!File.Exists(backupPath))
            {
                File.Copy(packPath, backupPath);
            }

            File.WriteAllBytes(packPath, newPackBytes);
            Logging.WriteLine($"[StaticDataPatcher] Successfully patched StaticData.pack ({newPackBytes.Length} bytes). Video cutscene URLs are now localized and viewable.", LogType.Info);

            return true;
        }
        catch (Exception ex)
        {
            Logging.WriteLine($"[StaticDataPatcher] Failed to patch StaticData.pack: {ex.Message}", LogType.Error);
            return false;
        }
    }

    private static byte[] PatchScenarioMovieTable(byte[] movieTableBytes, byte[] oldLink)
    {
        byte[] patched = (byte[])movieTableBytes.Clone();
        string[] languages = ["en", "ja", "ko", "zh-TW", "zh-CN", "de", "th", "fr"];

        foreach (string lang in languages)
        {
            byte[] idBytes = MakeMemoryPackString($"coinrushshowdown_{lang}");
            int idIndex = IndexOf(patched, idBytes);
            if (idIndex == -1) continue;

            string targetLang = (lang is "en" or "ja" or "ko") ? lang : "en";
            string newUrl = $"https://cloud.nikke-kr.com/media/coinrushshowdown/coinrushshowdown_{targetLang}.mp4";
            byte[] newLink = MakeMemoryPackString(newUrl);

            int linkIndex = IndexOf(patched, oldLink, idIndex);
            if (linkIndex != -1 && linkIndex - idIndex < 300)
            {
                byte[] next = new byte[patched.Length - oldLink.Length + newLink.Length];
                Buffer.BlockCopy(patched, 0, next, 0, linkIndex);
                Buffer.BlockCopy(newLink, 0, next, linkIndex, newLink.Length);
                Buffer.BlockCopy(patched, linkIndex + oldLink.Length, next, linkIndex + newLink.Length, patched.Length - (linkIndex + oldLink.Length));
                patched = next;
            }
        }

        return patched;
    }

    private static byte[] MakeMemoryPackString(string s)
    {
        byte[] sBytes = Encoding.UTF8.GetBytes(s);
        int u16Len = s.Length;
        int invLen = ~u16Len;
        byte[] result = new byte[8 + sBytes.Length];
        BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(0, 4), invLen);
        BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(4, 4), sBytes.Length);
        Buffer.BlockCopy(sBytes, 0, result, 8, sBytes.Length);
        return result;
    }

    private static int IndexOf(byte[] source, byte[] pattern, int startIndex = 0)
    {
        ReadOnlySpan<byte> src = source.AsSpan(startIndex);
        ReadOnlySpan<byte> pat = pattern;
        int idx = src.IndexOf(pat);
        return idx == -1 ? -1 : startIndex + idx;
    }
}
