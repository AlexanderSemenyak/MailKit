﻿//
// MessageSummaryFetchedEventArgs.cs
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

namespace MailKit {
	/// <summary>
	/// Event args used when a message summary has been fetched from a folder.
	/// </summary>
	/// <remarks>
	/// Event args used when a message summary has been fetched from a folder.
	/// </remarks>
	public class MessageSummaryFetchedEventArgs : EventArgs
	{
		/// <summary>
		/// Initializes a new instance of the <see cref="MailKit.MessageSummaryFetchedEventArgs"/> class.
		/// </summary>
		/// <remarks>
		/// Creates a new <see cref="MessageSummaryFetchedEventArgs"/>
		/// </remarks>
		/// <param name="message">The message summary.</param>
		/// <exception cref="System.ArgumentNullException">
		/// <paramref name="message"/> is <see langword="null" />.
		/// </exception>
		public MessageSummaryFetchedEventArgs (IMessageSummary message)
		{
			if (message == null)
				throw new ArgumentNullException (nameof (message));

			Message = message;
		}

		/// <summary>
		/// Get the message summary.
		/// </summary>
		/// <remarks>
		/// Gets the message summary.
		/// </remarks>
		/// <value>The message summary.</value>
		public IMessageSummary Message {
			get; private set;
		}
	}
}
