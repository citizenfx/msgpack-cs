using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security;

namespace CitizenFX.Core
{
	/// <summary>
	/// A byte string that can be used to interop with native code.
	/// It does this by using byte sized characters and guaranteeing that it's null terminated, equivalent to a (non-wide) c-string
	/// </summary>
	public class CString
	{
		internal readonly byte[] value;

		[SecurityCritical]
		private CString(byte[] str) => value = str;

		public unsafe CString(string str)
		{
			fixed (char* c = str)
			{
				if (str != null)
				{
					int strLength = str.Length;
					int byteCount = CString.UTF8EncodeLength(c, strLength);
					byte[] bytes = new byte[byteCount + 1]; // zero initializes all, so last byte will be and stay 0x0

					fixed (byte* b = bytes)
						CString.UTF8Encode(b, c, strLength);

					value = bytes;
				}
				else
					value = null;
			}
		}

		/// <summary>
		/// CString with a copy of str, limited to given length.
		/// </summary>
		/// <returns>a null terminated CString or null if parameter str == null</returns>
		[SecurityCritical]
		internal static unsafe CString Create(byte* str, uint length)
		{
			if (str != null)
			{
				byte[] array = new byte[length + 1];
				Marshal.Copy((IntPtr)str, array, 0, (int)length);

				return new CString(array);
			}

			return null;
		}

		public static explicit operator CString(string str) => str != null ? new CString(str) : null;

		public static bool IsNull(CString str) => !(str?.value != null);
		public static bool IsNullOrEmpty(CString str) => !(str?.value?.Length != 1);

		public static unsafe int UTF8EncodeLength(char* src, int length)
		{
			int byteCount = 0;
			char* end = src + length;
			while (src < end)
			{
				char c1 = *src;
				if (c1 < 0x80)
				{
					// ASCII character: requires 1 byte in UTF8.
					byteCount++;
				}
				else if (c1 < 0x800)
				{
					// Characters in the range [0x80, 0x7FF]: require 2 bytes in UTF8.
					byteCount += 2;
				}
				else if (c1 >= 0xD800 && c1 <= 0xDBFF) // High surrogate
				{
					if (src + 1 < end)
					{
						char c2 = *(src + 1);
						if (c2 >= 0xDC00 && c2 <= 0xDFFF)
						{
							// Valid surrogate pair: requires 4 bytes in UTF8.
							byteCount += 4;
							src++; // Skip the low surrogate as it's processed together with the high surrogate.
						}
						else
						{
							// Invalid surrogate pair: encode using replacement character U+FFFD (3 bytes).
							byteCount += 3;
						}
					}
					else
					{
						// Missing low surrogate: encode using replacement character U+FFFD (3 bytes).
						byteCount += 3;
					}
				}
				else if (c1 >= 0xDC00 && c1 <= 0xDFFF)
				{
					// Lone low surrogate: encode using replacement character U+FFFD (3 bytes).
					byteCount += 3;
				}
				else
				{
					// All other BMP characters (non-surrogate): require 3 bytes in UTF8.
					byteCount += 3;
				}
				src++;
			}
			return byteCount;
		}

		public static unsafe int UTF8Encode(byte* dst, char* src, int length)
		{
			byte* start = dst;
			char* end = src + length;
			while (src < end)
			{
				char c1 = *src;
				if (c1 < 0x80)
				{
					// 1-byte ASCII: direct assignment.
					*dst++ = (byte)c1;
				}
				else if (c1 < 0x800)
				{
					// 2-byte encoding for characters in the range [0x80, 0x7FF].
					*dst++ = (byte)(0xC0 | (c1 >> 6));
					*dst++ = (byte)(0x80 | (c1 & 0x3F));
				}
				else if (c1 >= 0xD800 && c1 <= 0xDBFF) // High surrogate
				{
					if (src + 1 < end)
					{
						char c2 = *(src + 1);
						if (c2 >= 0xDC00 && c2 <= 0xDFFF)
						{
							// Valid surrogate pair: compute Unicode code point and encode in 4 bytes.
							int codepoint = 0x10000 + (((c1 - 0xD800) << 10) | (c2 - 0xDC00));
							*dst++ = (byte)(0xF0 | (codepoint >> 18));
							*dst++ = (byte)(0x80 | ((codepoint >> 12) & 0x3F));
							*dst++ = (byte)(0x80 | ((codepoint >> 6) & 0x3F));
							*dst++ = (byte)(0x80 | (codepoint & 0x3F));
							src++; // Skip the low surrogate as it has been processed.
						}
						else
						{
							// Invalid surrogate pair: encode replacement character U+FFFD (0xEF, 0xBF, 0xBD).
							*dst++ = 0xEF;
							*dst++ = 0xBF;
							*dst++ = 0xBD;
						}
					}
					else
					{
						// Missing low surrogate: encode replacement character U+FFFD.
						*dst++ = 0xEF;
						*dst++ = 0xBF;
						*dst++ = 0xBD;
					}
				}
				else if (c1 >= 0xDC00 && c1 <= 0xDFFF)
				{
					// Lone low surrogate: encode replacement character U+FFFD.
					*dst++ = 0xEF;
					*dst++ = 0xBF;
					*dst++ = 0xBD;
				}
				else
				{
					// 3-byte encoding for all other BMP characters.
					*dst++ = (byte)(0xE0 | (c1 >> 12));
					*dst++ = (byte)(0x80 | ((c1 >> 6) & 0x3F));
					*dst++ = (byte)(0x80 | (c1 & 0x3F));
				}
				src++;
			}
			return (int)(dst - start);
		}

		/// <summary>
		/// Compares a CString to a C# string only considering ASCII characters (7 bits)
		/// any non 7 bit characters on either side will return false
		/// </summary>
		/// <param name="str">string to compare</param>
		/// <returns>true if both sides have equivalent characters in the ASCII character space</returns>
		public bool CompareASCII(string str)
		{
			return CompareASCII(this, str);
		}

		/// <summary>
		/// Compares a CString to a C# string only considering ASCII characters (7 bits)
		/// any non 7 bit characters on either side will return false
		/// </summary>
		/// <param name="left">CString to compare</param>
		/// <param name="right">string to compare</param>
		/// <returns>true if both sides have equivalent characters in the ASCII character space</returns>
		[SecuritySafeCritical]
		public static unsafe bool CompareASCII(CString left, string right)
		{
			if (left == null && right == null)
				return true;
			else
			{
				fixed (byte* lPin = left.value)
				fixed (char* rPin = right)
				{
					byte* l = lPin, lEnd = lPin + left.value.Length - 1;
					char* r = rPin, rEnd = rPin + right.Length;
					while (l < lEnd && r < rEnd)
					{
						char c1 = *r++;
						if (c1 > 0x7F || c1 != *l++)
							return false;
					}
				}

				return true;
			}
		}

		/// <inheritdoc cref="CompareASCII(CString, string)"/>
		/// <remarks>Case insensitive variant of <see cref="CompareASCII(CString, string)"/></remarks>
		[SecuritySafeCritical]
		public static unsafe bool CompareASCIICaseInsensitive(CString left, string right)
		{
			if (left == null && right == null)
				return true;
			else
			{
				fixed (byte* lPin = left.value)
				fixed (char* rPin = right)
				{
					byte* l = lPin, lEnd = lPin + left.value.Length - 1;
					char* r = rPin, rEnd = rPin + right.Length;
					while (l < lEnd && r < rEnd)
					{
						char c1 = *r++;
						if (c1 > 0x7F || !ToLowerEquals((byte)c1, *l++))
							return false;
					}
				}

				return true;
			}
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private static bool ToLowerEquals(byte l, byte r)
		{
			const uint offset = 'A';
			const byte toLower = 'a' - 'A';

			if (l - offset < 26)
				l += toLower;

			if (r - offset < 26)
				r += toLower;

			return l == r;
		}
	}
}
