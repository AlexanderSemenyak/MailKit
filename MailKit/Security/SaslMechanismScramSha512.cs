﻿//
// SaslMechanismScramSha512.cs
//
// Author: Jeffrey Stedfast <jestedfa@microsoft.com>
//
// Copyright (c) 2013-2025 .NET Foundation and Contributors
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.
//

using System;
using System.Net;
using System.Security.Cryptography;

namespace MailKit.Security {
	/// <summary>
	/// The SCRAM-SHA-512 SASL mechanism.
	/// </summary>
	/// <remarks>
	/// A salted challenge/response SASL mechanism that uses the HMAC SHA-512 algorithm.
	/// </remarks>
	public class SaslMechanismScramSha512 : SaslMechanismScramBase
	{
		/// <summary>
		/// Initializes a new instance of the <see cref="MailKit.Security.SaslMechanismScramSha512"/> class.
		/// </summary>
		/// <remarks>
		/// Creates a new SCRAM-SHA-512 SASL context.
		/// </remarks>
		/// <param name="credentials">The user's credentials.</param>
		/// <exception cref="System.ArgumentNullException">
		/// <paramref name="credentials"/> is <see langword="null" />.
		/// </exception>
		public SaslMechanismScramSha512 (NetworkCredential credentials) : base (credentials)
		{
		}

		/// <summary>
		/// Initializes a new instance of the <see cref="MailKit.Security.SaslMechanismScramSha512"/> class.
		/// </summary>
		/// <remarks>
		/// Creates a new SCRAM-SHA-512 SASL context.
		/// </remarks>
		/// <param name="userName">The user name.</param>
		/// <param name="password">The password.</param>
		/// <exception cref="System.ArgumentNullException">
		/// <para><paramref name="userName"/> is <see langword="null" />.</para>
		/// <para>-or-</para>
		/// <para><paramref name="password"/> is <see langword="null" />.</para>
		/// </exception>
		public SaslMechanismScramSha512 (string userName, string password) : base (userName, password)
		{
		}

		/// <summary>
		/// Get the name of the SASL mechanism.
		/// </summary>
		/// <remarks>
		/// Gets the name of the SASL mechanism.
		/// </remarks>
		/// <value>The name of the SASL mechanism.</value>
		public override string MechanismName {
			get { return "SCRAM-SHA-512"; }
		}

		/// <summary>
		/// Create the HMAC context.
		/// </summary>
		/// <remarks>
		/// Creates the HMAC context using the secret key.
		/// </remarks>
		/// <returns>The HMAC context.</returns>
		/// <param name="key">The secret key.</param>
		protected override KeyedHashAlgorithm CreateHMAC (byte[] key)
		{
			return new HMACSHA512 (key);
		}

		/// <summary>
		/// Apply the cryptographic hash function.
		/// </summary>
		/// <remarks>
		/// H(str): Apply the cryptographic hash function to the octet string
		/// "str", producing an octet string as a result. The size of the
		/// result depends on the hash result size for the hash function in
		/// use.
		/// </remarks>
		/// <returns>The results of the hash.</returns>
		/// <param name="str">The string.</param>
		protected override byte[] Hash (byte[] str)
		{
			using (var sha512 = SHA512.Create ())
				return sha512.ComputeHash (str);
		}
	}

	/// <summary>
	/// The SCRAM-SHA-512-PLUS SASL mechanism.
	/// </summary>
	/// <remarks>
	/// A salted challenge/response SASL mechanism that uses the HMAC SHA-512 algorithm and Transport Layer Security (TLS) channel binding.
	/// </remarks>
	public class SaslMechanismScramSha512Plus : SaslMechanismScramSha512
	{
		/// <summary>
		/// Initializes a new instance of the <see cref="MailKit.Security.SaslMechanismScramSha512Plus"/> class.
		/// </summary>
		/// <remarks>
		/// Creates a new SCRAM-SHA-512-PLUS SASL context.
		/// </remarks>
		/// <param name="credentials">The user's credentials.</param>
		/// <exception cref="System.ArgumentNullException">
		/// <paramref name="credentials"/> is <see langword="null" />.
		/// </exception>
		public SaslMechanismScramSha512Plus (NetworkCredential credentials) : base (credentials)
		{
		}

		/// <summary>
		/// Initializes a new instance of the <see cref="MailKit.Security.SaslMechanismScramSha512Plus"/> class.
		/// </summary>
		/// <remarks>
		/// Creates a new SCRAM-SHA-512-PLUS SASL context.
		/// </remarks>
		/// <param name="userName">The user name.</param>
		/// <param name="password">The password.</param>
		/// <exception cref="System.ArgumentNullException">
		/// <para><paramref name="userName"/> is <see langword="null" />.</para>
		/// <para>-or-</para>
		/// <para><paramref name="password"/> is <see langword="null" />.</para>
		/// </exception>
		public SaslMechanismScramSha512Plus (string userName, string password) : base (userName, password)
		{
		}

		/// <summary>
		/// Get the name of the SASL mechanism.
		/// </summary>
		/// <remarks>
		/// Gets the name of the SASL mechanism.
		/// </remarks>
		/// <value>The name of the SASL mechanism.</value>
		public override string MechanismName {
			get { return "SCRAM-SHA-512-PLUS"; }
		}

		/// <summary>
		/// Get whether or not the SASL mechanism supports channel binding.
		/// </summary>
		/// <remarks>
		/// Gets whether or not the SASL mechanism supports channel binding.
		/// </remarks>
		/// <value><see langword="true" /> if the SASL mechanism supports channel binding; otherwise, <see langword="false" />.</value>
		public override bool SupportsChannelBinding {
			get { return true; }
		}
	}
}
