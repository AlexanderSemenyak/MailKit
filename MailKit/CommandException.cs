﻿//
// CommandException.cs
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
#if SERIALIZABLE
using System.Security;
using System.Runtime.Serialization;
#endif

namespace MailKit {
	/// <summary>
	/// The exception that is thrown when there is a command error.
	/// </summary>
	/// <remarks>
	/// A <see cref="CommandException"/> can be thrown by any of the various client
	/// methods in MailKit. Unlike a <see cref="ProtocolException"/>, a
	/// <see cref="CommandException"/> is typically non-fatal (meaning that it does
	/// not force the client to disconnect).
	/// </remarks>
#if SERIALIZABLE
	[Serializable]
#endif
	public abstract class CommandException : Exception
	{
#if SERIALIZABLE
		/// <summary>
		/// Initializes a new instance of the <see cref="MailKit.CommandException"/> class.
		/// </summary>
		/// <remarks>
		/// Creates a new <see cref="CommandException"/>.
		/// </remarks>
		/// <param name="info">The serialization info.</param>
		/// <param name="context">The streaming context.</param>
		/// <exception cref="System.ArgumentNullException">
		/// <paramref name="info"/> is <see langword="null" />.
		/// </exception>
		[SecuritySafeCritical]
		[Obsolete ("This API supports obsolete formatter-based serialization. It should not be called or extended by application code.")]
		protected CommandException (SerializationInfo info, StreamingContext context) : base (info, context)
		{
		}
#endif

		/// <summary>
		/// Initializes a new instance of the <see cref="MailKit.CommandException"/> class.
		/// </summary>
		/// <remarks>
		/// Creates a new <see cref="CommandException"/>.
		/// </remarks>
		/// <param name="message">The error message.</param>
		/// <param name="innerException">An inner exception.</param>
		protected CommandException (string message, Exception innerException) : base (message, innerException)
		{
			HelpLink = "https://github.com/jstedfast/MailKit/blob/master/FAQ.md#ProtocolLog";
		}

		/// <summary>
		/// Initializes a new instance of the <see cref="MailKit.CommandException"/> class.
		/// </summary>
		/// <remarks>
		/// Creates a new <see cref="CommandException"/>.
		/// </remarks>
		/// <param name="message">The error message.</param>
		protected CommandException (string message) : base (message)
		{
			HelpLink = "https://github.com/jstedfast/MailKit/blob/master/FAQ.md#ProtocolLog";
		}

		/// <summary>
		/// Initializes a new instance of the <see cref="MailKit.CommandException"/> class.
		/// </summary>
		/// <remarks>
		/// Creates a new <see cref="CommandException"/>.
		/// </remarks>
		protected CommandException ()
		{
			HelpLink = "https://github.com/jstedfast/MailKit/blob/master/FAQ.md#ProtocolLog";
		}
	}
}
