﻿//
// ImapFolderTests.cs
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

using System.Net;
using System.Text;
using System.Globalization;

using MimeKit;

using MailKit;
using MailKit.Search;
using MailKit.Security;
using MailKit.Net.Imap;

namespace UnitTests.Net.Imap {
	[TestFixture]
	public class ImapFolderTests
	{
		static readonly Encoding Latin1 = Encoding.GetEncoding (28591);

		static MimeMessage CreateThreadableMessage (string subject, string msgid, string references, DateTimeOffset date)
		{
			var message = new MimeMessage ();
			message.From.Add (new MailboxAddress ("Unit Tests", "unit-tests@mimekit.net"));
			message.To.Add (new MailboxAddress ("Unit Tests", "unit-tests@mimekit.net"));
			message.MessageId = msgid;
			message.Subject = subject;
			message.Date = date;

			if (references != null) {
				foreach (var reference in references.Split (' '))
					message.References.Add (reference);
			}

			message.Body = new TextPart ("plain") { Text = "This is the message body.\r\n" };

			return message;
		}

		static Stream GetResourceStream (string name)
		{
			return typeof (ImapFolderTests).Assembly.GetManifestResourceStream ("UnitTests.Net.Imap.Resources." + name);
		}

		[Test]
		public void TestArgumentExceptions ()
		{
			var commands = new List<ImapReplayCommand> {
				new ImapReplayCommand ("", "dovecot.greeting.txt"),
				new ImapReplayCommand ("A00000000 LOGIN username password\r\n", "dovecot.authenticate+gmail-capabilities.txt"),
				new ImapReplayCommand ("A00000001 NAMESPACE\r\n", "dovecot.namespace.txt"),
				new ImapReplayCommand ("A00000002 LIST \"\" \"INBOX\" RETURN (SUBSCRIBED CHILDREN)\r\n", "dovecot.list-inbox.txt"),
				new ImapReplayCommand ("A00000003 LIST (SPECIAL-USE) \"\" \"*\" RETURN (SUBSCRIBED CHILDREN)\r\n", "dovecot.list-special-use.txt"),
				new ImapReplayCommand ("A00000004 SELECT INBOX (CONDSTORE)\r\n", "common.select-inbox.txt")
			};

			using (var client = new ImapClient () { TagPrefix = 'A' }) {
				var credentials = new NetworkCredential ("username", "password");

				try {
					client.Connect (new ImapReplayStream (commands, false), "localhost", 143, SecureSocketOptions.None);
				} catch (Exception ex) {
					Assert.Fail ($"Did not expect an exception in Connect: {ex}");
				}

				// Note: we do not want to use SASL at all...
				client.AuthenticationMechanisms.Clear ();

				try {
					client.Authenticate (credentials);
				} catch (Exception ex) {
					Assert.Fail ($"Did not expect an exception in Authenticate: {ex}");
				}

				var personal = client.GetFolder (client.PersonalNamespaces[0]);
				var multiappend = new List<IAppendRequest> ();
				var dates = new List<DateTimeOffset> ();
				var messages = new List<MimeMessage> ();
				var flags = new List<MessageFlags> ();
				var now = DateTimeOffset.Now;
				var uid = new UniqueId (1);
				ReplaceRequest replace = null;

				messages.Add (CreateThreadableMessage ("A", "<a@mimekit.net>", null, now.AddMinutes (-7)));
				messages.Add (CreateThreadableMessage ("B", "<b@mimekit.net>", "<a@mimekit.net>", now.AddMinutes (-6)));
				messages.Add (CreateThreadableMessage ("C", "<c@mimekit.net>", "<a@mimekit.net> <b@mimekit.net>", now.AddMinutes (-5)));
				messages.Add (CreateThreadableMessage ("D", "<d@mimekit.net>", "<a@mimekit.net>", now.AddMinutes (-4)));
				messages.Add (CreateThreadableMessage ("E", "<e@mimekit.net>", "<c@mimekit.net> <x@mimekit.net> <y@mimekit.net> <z@mimekit.net>", now.AddMinutes (-3)));
				messages.Add (CreateThreadableMessage ("F", "<f@mimekit.net>", "<b@mimekit.net>", now.AddMinutes (-2)));
				messages.Add (CreateThreadableMessage ("G", "<g@mimekit.net>", null, now.AddMinutes (-1)));
				messages.Add (CreateThreadableMessage ("H", "<h@mimekit.net>", null, now));

				for (int i = 0; i < messages.Count; i++) {
					dates.Add (DateTimeOffset.Now);
					flags.Add (MessageFlags.Seen);
					multiappend.Add (new AppendRequest (messages[i], flags[i], dates[i]));
					replace ??= new ReplaceRequest (messages[i], flags[i], dates[i]);
				}

				Assert.That (client.Inbox.SyncRoot, Is.InstanceOf<ImapEngine> (), "SyncRoot");

				var inbox = (ImapFolder) client.Inbox;
				inbox.Open (FolderAccess.ReadWrite);

				// ImapFolder .ctor
				Assert.Throws<ArgumentNullException> (() => new ImapFolder (null));

				// Open
				Assert.Throws<ArgumentOutOfRangeException> (() => inbox.Open ((FolderAccess) 500));
				Assert.Throws<ArgumentOutOfRangeException> (() => inbox.Open ((FolderAccess) 500, 0, 0, UniqueIdRange.All));
				Assert.Throws<ArgumentNullException> (() => inbox.Open (FolderAccess.ReadOnly, 0, 0, null));
				Assert.ThrowsAsync<ArgumentOutOfRangeException> (() => inbox.OpenAsync ((FolderAccess) 500));
				Assert.ThrowsAsync<ArgumentOutOfRangeException> (() => inbox.OpenAsync ((FolderAccess) 500, 0, 0, UniqueIdRange.All));
				Assert.ThrowsAsync<ArgumentNullException> (() => inbox.OpenAsync (FolderAccess.ReadOnly, 0, 0, null));

				// Create
				Assert.Throws<ArgumentNullException> (() => inbox.Create (null, true));
				Assert.Throws<ArgumentException> (() => inbox.Create (string.Empty, true));
				Assert.Throws<ArgumentException> (() => inbox.Create ("Folder./Name", true));
				Assert.Throws<ArgumentNullException> (() => inbox.Create (null, SpecialFolder.All));
				Assert.Throws<ArgumentException> (() => inbox.Create (string.Empty, SpecialFolder.All));
				Assert.Throws<ArgumentException> (() => inbox.Create ("Folder./Name", SpecialFolder.All));
				Assert.Throws<ArgumentNullException> (() => inbox.Create (null, new SpecialFolder[] { SpecialFolder.All }));
				Assert.Throws<ArgumentException> (() => inbox.Create (string.Empty, new SpecialFolder[] { SpecialFolder.All }));
				Assert.Throws<ArgumentException> (() => inbox.Create ("Folder./Name", new SpecialFolder[] { SpecialFolder.All }));
				Assert.Throws<ArgumentNullException> (() => inbox.Create ("ValidName", null));
				Assert.Throws<NotSupportedException> (() => inbox.Create ("ValidName", SpecialFolder.All));
				Assert.ThrowsAsync<ArgumentNullException> (() => inbox.CreateAsync (null, true));
				Assert.ThrowsAsync<ArgumentException> (() => inbox.CreateAsync (string.Empty, true));
				Assert.ThrowsAsync<ArgumentException> (() => inbox.CreateAsync ("Folder./Name", true));
				Assert.ThrowsAsync<ArgumentNullException> (() => inbox.CreateAsync (null, SpecialFolder.All));
				Assert.ThrowsAsync<ArgumentException> (() => inbox.CreateAsync (string.Empty, SpecialFolder.All));
				Assert.ThrowsAsync<ArgumentException> (() => inbox.CreateAsync ("Folder./Name", SpecialFolder.All));
				Assert.ThrowsAsync<ArgumentNullException> (() => inbox.CreateAsync (null, new SpecialFolder[] { SpecialFolder.All }));
				Assert.ThrowsAsync<ArgumentException> (() => inbox.CreateAsync (string.Empty, new SpecialFolder[] { SpecialFolder.All }));
				Assert.ThrowsAsync<ArgumentException> (() => inbox.CreateAsync ("Folder./Name", new SpecialFolder[] { SpecialFolder.All }));
				Assert.ThrowsAsync<ArgumentNullException> (() => inbox.CreateAsync ("ValidName", null));
				Assert.ThrowsAsync<NotSupportedException> (() => inbox.CreateAsync ("ValidName", SpecialFolder.All));

				// Rename
				Assert.Throws<ArgumentNullException> (() => inbox.Rename (null, "NewName"));
				Assert.Throws<ArgumentNullException> (() => inbox.Rename (personal, null));
				Assert.Throws<ArgumentException> (() => inbox.Rename (personal, string.Empty));
				Assert.ThrowsAsync<ArgumentNullException> (() => inbox.RenameAsync (null, "NewName"));
				Assert.ThrowsAsync<ArgumentNullException> (() => inbox.RenameAsync (personal, null));
				Assert.ThrowsAsync<ArgumentException> (() => inbox.RenameAsync (personal, string.Empty));

				// GetSubfolder
				Assert.Throws<ArgumentNullException> (() => inbox.GetSubfolder (null));
				Assert.Throws<ArgumentException> (() => inbox.GetSubfolder (string.Empty));
				Assert.ThrowsAsync<ArgumentNullException> (() => inbox.GetSubfolderAsync (null));
				Assert.ThrowsAsync<ArgumentException> (() => inbox.GetSubfolderAsync (string.Empty));

				// GetMetadata
				Assert.Throws<ArgumentNullException> (() => client.GetMetadata (null, new MetadataTag[] { MetadataTag.PrivateComment }));
				Assert.Throws<ArgumentNullException> (() => client.GetMetadata (new MetadataOptions (), null));
				Assert.ThrowsAsync<ArgumentNullException> (() => client.GetMetadataAsync (null, new MetadataTag[] { MetadataTag.PrivateComment }));
				Assert.ThrowsAsync<ArgumentNullException> (() => client.GetMetadataAsync (new MetadataOptions (), null));
				Assert.Throws<ArgumentNullException> (() => inbox.GetMetadata (null, new MetadataTag[] { MetadataTag.PrivateComment }));
				Assert.Throws<ArgumentNullException> (() => inbox.GetMetadata (new MetadataOptions (), null));
				Assert.ThrowsAsync<ArgumentNullException> (() => inbox.GetMetadataAsync (null, new MetadataTag[] { MetadataTag.PrivateComment }));
				Assert.ThrowsAsync<ArgumentNullException> (() => inbox.GetMetadataAsync (new MetadataOptions (), null));

				// SetMetadata
				Assert.Throws<ArgumentNullException> (() => client.SetMetadata (null));
				Assert.ThrowsAsync<ArgumentNullException> (() => client.SetMetadataAsync (null));
				Assert.Throws<ArgumentNullException> (() => inbox.SetMetadata (null));
				Assert.ThrowsAsync<ArgumentNullException> (() => inbox.SetMetadataAsync (null));

				// Expunge
				Assert.Throws<ArgumentNullException> (() => inbox.Expunge (null));
				Assert.ThrowsAsync<ArgumentNullException> (() => inbox.ExpungeAsync (null));

				// Append
				Assert.Throws<ArgumentNullException> (() => inbox.Append ((MimeMessage) null));
				Assert.ThrowsAsync<ArgumentNullException> (() => inbox.AppendAsync ((MimeMessage) null));
				Assert.Throws<ArgumentNullException> (() => inbox.Append (null, messages[0]));
				Assert.ThrowsAsync<ArgumentNullException> (() => inbox.AppendAsync (null, messages[0]));
				Assert.Throws<ArgumentNullException> (() => inbox.Append (FormatOptions.Default, (MimeMessage) null));
				Assert.ThrowsAsync<ArgumentNullException> (() => inbox.AppendAsync (FormatOptions.Default, (MimeMessage) null));
				Assert.Throws<ArgumentNullException> (() => inbox.Append (null, MessageFlags.None, DateTimeOffset.Now));
				Assert.ThrowsAsync<ArgumentNullException> (() => inbox.AppendAsync (null, MessageFlags.None, DateTimeOffset.Now));
				Assert.Throws<ArgumentNullException> (() => inbox.Append (null, messages[0], MessageFlags.None, DateTimeOffset.Now));
				Assert.ThrowsAsync<ArgumentNullException> (() => inbox.AppendAsync (null, messages[0], MessageFlags.None, DateTimeOffset.Now));
				Assert.Throws<ArgumentNullException> (() => inbox.Append (FormatOptions.Default, null, MessageFlags.None, DateTimeOffset.Now));
				Assert.ThrowsAsync<ArgumentNullException> (() => inbox.AppendAsync (FormatOptions.Default, null, MessageFlags.None, DateTimeOffset.Now));
				Assert.Throws<ArgumentNullException> (() => inbox.Append ((IAppendRequest) null));
				Assert.ThrowsAsync<ArgumentNullException> (() => inbox.AppendAsync ((IAppendRequest) null));
				Assert.Throws<ArgumentNullException> (() => inbox.Append (null, new AppendRequest (messages[0])));
				Assert.ThrowsAsync<ArgumentNullException> (() => inbox.AppendAsync (null, new AppendRequest (messages[0])));
				Assert.Throws<ArgumentNullException> (() => inbox.Append (FormatOptions.Default, (IAppendRequest) null));
				Assert.ThrowsAsync<ArgumentNullException> (() => inbox.AppendAsync (FormatOptions.Default, (IAppendRequest) null));

				// MultiAppend
				Assert.Throws<ArgumentNullException> (() => inbox.Append (null, flags));
				Assert.ThrowsAsync<ArgumentNullException> (() => inbox.AppendAsync (null, flags));
				Assert.Throws<ArgumentException> (() => inbox.Append (new MimeMessage[] { null }, flags));
				Assert.ThrowsAsync<ArgumentException> (() => inbox.AppendAsync (new MimeMessage[] { null }, flags));
				Assert.Throws<ArgumentNullException> (() => inbox.Append (messages, null));
				Assert.ThrowsAsync<ArgumentNullException> (() => inbox.AppendAsync (messages, null));
				Assert.Throws<ArgumentException> (() => inbox.Append (messages, new MessageFlags[messages.Count - 1]));
				Assert.ThrowsAsync<ArgumentException> (() => inbox.AppendAsync (messages, new MessageFlags[messages.Count - 1]));
				Assert.Throws<ArgumentNullException> (() => inbox.Append (null, messages, flags));
				Assert.ThrowsAsync<ArgumentNullException> (() => inbox.AppendAsync (null, messages, flags));
				Assert.Throws<ArgumentNullException> (() => inbox.Append (FormatOptions.Default, null, flags));
				Assert.ThrowsAsync<ArgumentNullException> (() => inbox.AppendAsync (FormatOptions.Default, null, flags));
				Assert.Throws<ArgumentException> (() => inbox.Append (FormatOptions.Default, new MimeMessage[] { null }, flags));
				Assert.ThrowsAsync<ArgumentException> (() => inbox.AppendAsync (FormatOptions.Default, new MimeMessage[] { null }, flags));
				Assert.Throws<ArgumentNullException> (() => inbox.Append (FormatOptions.Default, messages, null));
				Assert.ThrowsAsync<ArgumentNullException> (() => inbox.AppendAsync (FormatOptions.Default, messages, null));
				Assert.Throws<ArgumentException> (() => inbox.Append (FormatOptions.Default, messages, new MessageFlags[messages.Count - 1]));
				Assert.ThrowsAsync<ArgumentException> (() => inbox.AppendAsync (FormatOptions.Default, messages, new MessageFlags[messages.Count - 1]));
				Assert.Throws<ArgumentNullException> (() => inbox.Append (null, flags, dates));
				Assert.ThrowsAsync<ArgumentNullException> (() => inbox.AppendAsync (null, flags, dates));
				Assert.Throws<ArgumentException> (() => inbox.Append (new MimeMessage[] { null }, flags, dates));
				Assert.ThrowsAsync<ArgumentException> (() => inbox.AppendAsync (new MimeMessage[] { null }, flags, dates));
				Assert.Throws<ArgumentNullException> (() => inbox.Append (messages, null, dates));
				Assert.ThrowsAsync<ArgumentNullException> (() => inbox.AppendAsync (messages, null, dates));
				Assert.Throws<ArgumentNullException> (() => inbox.Append (messages, flags, null));
				Assert.ThrowsAsync<ArgumentNullException> (() => inbox.AppendAsync (messages, flags, null));
				Assert.Throws<ArgumentException> (() => inbox.Append (messages, new MessageFlags[messages.Count - 1], dates));
				Assert.ThrowsAsync<ArgumentException> (() => inbox.AppendAsync (messages, new MessageFlags[messages.Count - 1], dates));
				Assert.Throws<ArgumentException> (() => inbox.Append (messages, flags, new DateTimeOffset[messages.Count - 1]));
				Assert.ThrowsAsync<ArgumentException> (() => inbox.AppendAsync (messages, flags, new DateTimeOffset[messages.Count - 1]));
				Assert.Throws<ArgumentNullException> (() => inbox.Append (null, messages, flags, dates));
				Assert.ThrowsAsync<ArgumentNullException> (() => inbox.AppendAsync (null, messages, flags, dates));
				Assert.Throws<ArgumentNullException> (() => inbox.Append (FormatOptions.Default, null, flags, dates));
				Assert.ThrowsAsync<ArgumentNullException> (() => inbox.AppendAsync (FormatOptions.Default, null, flags, dates));
				Assert.Throws<ArgumentException> (() => inbox.Append (FormatOptions.Default, new MimeMessage[] { null }, flags, dates));
				Assert.ThrowsAsync<ArgumentException> (() => inbox.AppendAsync (FormatOptions.Default, new MimeMessage[] { null }, flags, dates));
				Assert.Throws<ArgumentNullException> (() => inbox.Append (FormatOptions.Default, messages, null, dates));
				Assert.ThrowsAsync<ArgumentNullException> (() => inbox.AppendAsync (FormatOptions.Default, messages, null, dates));
				Assert.Throws<ArgumentNullException> (() => inbox.Append (FormatOptions.Default, messages, flags, null));
				Assert.ThrowsAsync<ArgumentNullException> (() => inbox.AppendAsync (FormatOptions.Default, messages, flags, null));
				Assert.Throws<ArgumentException> (() => inbox.Append (FormatOptions.Default, messages, new MessageFlags[messages.Count - 1], dates));
				Assert.ThrowsAsync<ArgumentException> (() => inbox.AppendAsync (FormatOptions.Default, messages, new MessageFlags[messages.Count - 1], dates));
				Assert.Throws<ArgumentException> (() => inbox.Append (FormatOptions.Default, messages, flags, new DateTimeOffset[messages.Count - 1]));
				Assert.ThrowsAsync<ArgumentException> (() => inbox.AppendAsync (FormatOptions.Default, messages, flags, new DateTimeOffset[messages.Count - 1]));
				Assert.Throws<ArgumentNullException> (() => inbox.Append ((IList<IAppendRequest>) null));
				Assert.ThrowsAsync<ArgumentNullException> (() => inbox.AppendAsync ((IList<IAppendRequest>) null));
				Assert.Throws<ArgumentNullException> (() => inbox.Append (null, multiappend));
				Assert.ThrowsAsync<ArgumentNullException> (() => inbox.AppendAsync (null, multiappend));
				Assert.Throws<ArgumentException> (() => inbox.Append (new IAppendRequest[1]));
				Assert.ThrowsAsync<ArgumentException> (() => inbox.AppendAsync (new IAppendRequest[1]));
				Assert.Throws<ArgumentException> (() => inbox.Append (FormatOptions.Default, new IAppendRequest[1]));
				Assert.ThrowsAsync<ArgumentException> (() => inbox.AppendAsync (FormatOptions.Default, new IAppendRequest[1]));

				// Replace
				Assert.Throws<ArgumentException> (() => inbox.Replace (UniqueId.Invalid, messages[0]));
				Assert.ThrowsAsync<ArgumentException> (() => inbox.ReplaceAsync (UniqueId.Invalid, messages[0]));
				Assert.Throws<ArgumentException> (() => inbox.Replace (UniqueId.Invalid, messages[0], MessageFlags.None, DateTimeOffset.Now));
				Assert.ThrowsAsync<ArgumentException> (() => inbox.ReplaceAsync (UniqueId.Invalid, messages[0], MessageFlags.None, DateTimeOffset.Now));
				Assert.Throws<ArgumentNullException> (() => inbox.Replace (uid, null));
				Assert.ThrowsAsync<ArgumentNullException> (() => inbox.ReplaceAsync (uid, null));
				Assert.Throws<ArgumentNullException> (() => inbox.Replace (uid, null, MessageFlags.None, DateTimeOffset.Now));
				Assert.ThrowsAsync<ArgumentNullException> (() => inbox.ReplaceAsync (uid, null, MessageFlags.None, DateTimeOffset.Now));
				Assert.Throws<ArgumentNullException> (() => inbox.Replace (null, uid, messages[0]));
				Assert.ThrowsAsync<ArgumentNullException> (() => inbox.ReplaceAsync (null, uid, messages[0]));
				Assert.Throws<ArgumentNullException> (() => inbox.Replace (null, uid, messages[0], MessageFlags.None, DateTimeOffset.Now));
				Assert.ThrowsAsync<ArgumentNullException> (() => inbox.ReplaceAsync (null, uid, messages[0], MessageFlags.None, DateTimeOffset.Now));
				Assert.Throws<ArgumentOutOfRangeException> (() => inbox.Replace (-1, messages[0]));
				Assert.ThrowsAsync<ArgumentOutOfRangeException> (() => inbox.ReplaceAsync (-1, messages[0]));
				Assert.Throws<ArgumentOutOfRangeException> (() => inbox.Replace (-1, messages[0], MessageFlags.None, DateTimeOffset.Now));
				Assert.ThrowsAsync<ArgumentOutOfRangeException> (() => inbox.ReplaceAsync (-1, messages[0], MessageFlags.None, DateTimeOffset.Now));
				Assert.Throws<ArgumentNullException> (() => inbox.Replace (0, null));
				Assert.ThrowsAsync<ArgumentNullException> (() => inbox.ReplaceAsync (0, null));
				Assert.Throws<ArgumentNullException> (() => inbox.Replace (0, null, MessageFlags.None, DateTimeOffset.Now));
				Assert.ThrowsAsync<ArgumentNullException> (() => inbox.ReplaceAsync (0, null, MessageFlags.None, DateTimeOffset.Now));
				Assert.Throws<ArgumentNullException> (() => inbox.Replace (null, 0, messages[0]));
				Assert.ThrowsAsync<ArgumentNullException> (() => inbox.ReplaceAsync (null, 0, messages[0]));
				Assert.Throws<ArgumentNullException> (() => inbox.Replace (null, 0, messages[0], MessageFlags.None, DateTimeOffset.Now));
				Assert.ThrowsAsync<ArgumentNullException> (() => inbox.ReplaceAsync (null, 0, messages[0], MessageFlags.None, DateTimeOffset.Now));
				Assert.Throws<ArgumentException> (() => inbox.Replace (UniqueId.Invalid, replace));
				Assert.ThrowsAsync<ArgumentException> (() => inbox.ReplaceAsync (UniqueId.Invalid, replace));
				Assert.Throws<ArgumentNullException> (() => inbox.Replace (UniqueId.MinValue, null));
				Assert.ThrowsAsync<ArgumentNullException> (() => inbox.ReplaceAsync (UniqueId.MinValue, null));
				Assert.Throws<ArgumentNullException> (() => inbox.Replace (null, UniqueId.MinValue, replace));
				Assert.ThrowsAsync<ArgumentNullException> (() => inbox.ReplaceAsync (null, UniqueId.MinValue, replace));
				Assert.Throws<ArgumentException> (() => inbox.Replace (FormatOptions.Default, UniqueId.Invalid, replace));
				Assert.ThrowsAsync<ArgumentException> (() => inbox.ReplaceAsync (FormatOptions.Default, UniqueId.Invalid, replace));
				Assert.Throws<ArgumentNullException> (() => inbox.Replace (FormatOptions.Default, UniqueId.MinValue, null));
				Assert.ThrowsAsync<ArgumentNullException> (() => inbox.ReplaceAsync (FormatOptions.Default, UniqueId.MinValue, null));
				Assert.Throws<ArgumentOutOfRangeException> (() => inbox.Replace (-1, replace));
				Assert.ThrowsAsync<ArgumentOutOfRangeException> (() => inbox.ReplaceAsync (-1, replace));
				Assert.Throws<ArgumentNullException> (() => inbox.Replace (0, null));
				Assert.ThrowsAsync<ArgumentNullException> (() => inbox.ReplaceAsync (0, null));
				Assert.Throws<ArgumentNullException> (() => inbox.Replace (null, 0, replace));
				Assert.ThrowsAsync<ArgumentNullException> (() => inbox.ReplaceAsync (null, 0, replace));
				Assert.Throws<ArgumentOutOfRangeException> (() => inbox.Replace (FormatOptions.Default, -1, replace));
				Assert.ThrowsAsync<ArgumentOutOfRangeException> (() => inbox.ReplaceAsync (FormatOptions.Default, -1, replace));
				Assert.Throws<ArgumentNullException> (() => inbox.Replace (FormatOptions.Default, 0, null));
				Assert.ThrowsAsync<ArgumentNullException> (() => inbox.ReplaceAsync (FormatOptions.Default, 0, null));

				// CopyTo
				Assert.Throws<ArgumentException> (() => inbox.CopyTo (UniqueId.Invalid, inbox));
				Assert.ThrowsAsync<ArgumentException> (() => inbox.CopyToAsync (UniqueId.Invalid, inbox));
				Assert.Throws<ArgumentNullException> (() => inbox.CopyTo (UniqueId.MinValue, null));
				Assert.ThrowsAsync<ArgumentNullException> (() => inbox.CopyToAsync (UniqueId.MinValue, null));
				Assert.Throws<ArgumentNullException> (() => inbox.CopyTo ((IList<UniqueId>) null, inbox));
				Assert.ThrowsAsync<ArgumentNullException> (() => inbox.CopyToAsync ((IList<UniqueId>) null, inbox));
				Assert.Throws<ArgumentNullException> (() => inbox.CopyTo (UniqueIdRange.All, null));
				Assert.ThrowsAsync<ArgumentNullException> (() => inbox.CopyToAsync (UniqueIdRange.All, null));
				Assert.Throws<ArgumentOutOfRangeException> (() => inbox.CopyTo (-1, inbox));
				Assert.ThrowsAsync<ArgumentOutOfRangeException> (() => inbox.CopyToAsync (-1, inbox));
				Assert.Throws<ArgumentNullException> (() => inbox.CopyTo (0, null));
				Assert.ThrowsAsync<ArgumentNullException> (() => inbox.CopyToAsync (0, null));
				Assert.Throws<ArgumentNullException> (() => inbox.CopyTo ((IList<int>) null, inbox));
				Assert.ThrowsAsync<ArgumentNullException> (() => inbox.CopyToAsync ((IList<int>) null, inbox));
				Assert.Throws<ArgumentNullException> (() => inbox.CopyTo (new int[] { 0 }, null));
				Assert.ThrowsAsync<ArgumentNullException> (() => inbox.CopyToAsync (new int[] { 0 }, null));

				// MoveTo
				Assert.Throws<ArgumentException> (() => inbox.MoveTo (UniqueId.Invalid, inbox));
				Assert.ThrowsAsync<ArgumentException> (() => inbox.MoveToAsync (UniqueId.Invalid, inbox));
				Assert.Throws<ArgumentNullException> (() => inbox.MoveTo (UniqueId.MinValue, null));
				Assert.ThrowsAsync<ArgumentNullException> (() => inbox.MoveToAsync (UniqueId.MinValue, null));
				Assert.Throws<ArgumentNullException> (() => inbox.MoveTo ((IList<UniqueId>) null, inbox));
				Assert.ThrowsAsync<ArgumentNullException> (() => inbox.MoveToAsync ((IList<UniqueId>) null, inbox));
				Assert.Throws<ArgumentNullException> (() => inbox.MoveTo (UniqueIdRange.All, null));
				Assert.ThrowsAsync<ArgumentNullException> (() => inbox.MoveToAsync (UniqueIdRange.All, null));
				Assert.Throws<ArgumentOutOfRangeException> (() => inbox.MoveTo (-1, inbox));
				Assert.ThrowsAsync<ArgumentOutOfRangeException> (() => inbox.MoveToAsync (-1, inbox));
				Assert.Throws<ArgumentNullException> (() => inbox.MoveTo (0, null));
				Assert.ThrowsAsync<ArgumentNullException> (() => inbox.MoveToAsync (0, null));
				Assert.Throws<ArgumentNullException> (() => inbox.MoveTo ((IList<int>) null, inbox));
				Assert.ThrowsAsync<ArgumentNullException> (() => inbox.MoveToAsync ((IList<int>) null, inbox));
				Assert.Throws<ArgumentNullException> (() => inbox.MoveTo (new int[] { 0 }, null));
				Assert.ThrowsAsync<ArgumentNullException> (() => inbox.MoveToAsync (new int[] { 0 }, null));

				client.Disconnect (false);

				foreach (var message in messages)
					message.Dispose ();
			}
		}

		[Test]
		public void TestNotSupportedExceptions ()
		{
			var commands = new List<ImapReplayCommand> {
				new ImapReplayCommand ("", "dovecot.greeting.txt"),
				new ImapReplayCommand ("A00000000 LOGIN username password\r\n", "dovecot.authenticate+gmail-capabilities.txt"),
				new ImapReplayCommand ("A00000001 NAMESPACE\r\n", "dovecot.namespace.txt"),
				new ImapReplayCommand ("A00000002 LIST \"\" \"INBOX\" RETURN (SUBSCRIBED CHILDREN)\r\n", "dovecot.list-inbox.txt"),
				new ImapReplayCommand ("A00000003 LIST (SPECIAL-USE) \"\" \"*\" RETURN (SUBSCRIBED CHILDREN)\r\n", "dovecot.list-special-use.txt"),
				//new ImapReplayCommand ("A00000004 SELECT INBOX\r\n", "common.select-inbox.txt")
			};

			using (var client = new ImapClient () { TagPrefix = 'A' }) {
				var credentials = new NetworkCredential ("username", "password");

				try {
					client.Connect (new ImapReplayStream (commands, false), "localhost", 143, SecureSocketOptions.None);
				} catch (Exception ex) {
					Assert.Fail ($"Did not expect an exception in Connect: {ex}");
				}

				// Note: we do not want to use SASL at all...
				client.AuthenticationMechanisms.Clear ();

				try {
					client.Authenticate (credentials);
				} catch (Exception ex) {
					Assert.Fail ($"Did not expect an exception in Authenticate: {ex}");
				}

				// disable all features
				client.Capabilities = ImapCapabilities.None;

				var personal = client.GetFolder (client.PersonalNamespaces[0]);
				var dates = new List<DateTimeOffset> ();
				var messages = new List<MimeMessage> ();
				var flags = new List<MessageFlags> ();
				var now = DateTimeOffset.Now;

				messages.Add (CreateThreadableMessage ("A", "<a@mimekit.net>", null, now.AddMinutes (-7)));
				messages.Add (CreateThreadableMessage ("B", "<b@mimekit.net>", "<a@mimekit.net>", now.AddMinutes (-6)));
				messages.Add (CreateThreadableMessage ("C", "<c@mimekit.net>", "<a@mimekit.net> <b@mimekit.net>", now.AddMinutes (-5)));
				messages.Add (CreateThreadableMessage ("D", "<d@mimekit.net>", "<a@mimekit.net>", now.AddMinutes (-4)));
				messages.Add (CreateThreadableMessage ("E", "<e@mimekit.net>", "<c@mimekit.net> <x@mimekit.net> <y@mimekit.net> <z@mimekit.net>", now.AddMinutes (-3)));
				messages.Add (CreateThreadableMessage ("F", "<f@mimekit.net>", "<b@mimekit.net>", now.AddMinutes (-2)));
				messages.Add (CreateThreadableMessage ("G", "<g@mimekit.net>", null, now.AddMinutes (-1)));
				messages.Add (CreateThreadableMessage ("H", "<h@mimekit.net>", null, now));

				for (int i = 0; i < messages.Count; i++) {
					dates.Add (DateTimeOffset.Now);
					flags.Add (MessageFlags.Seen);
				}

				Assert.That (client.Inbox.SyncRoot, Is.InstanceOf<ImapEngine> (), "SyncRoot");

				var inbox = (ImapFolder) client.Inbox;

				// Open
				Assert.Throws<NotSupportedException> (() => inbox.Open (FolderAccess.ReadOnly, 0, 0, UniqueIdRange.All));
				Assert.ThrowsAsync<NotSupportedException> (() => inbox.OpenAsync (FolderAccess.ReadOnly, 0, 0, UniqueIdRange.All));

				// Create
				Assert.Throws<NotSupportedException> (() => inbox.Create ("Folder", SpecialFolder.All));
				Assert.ThrowsAsync<NotSupportedException> (() => inbox.CreateAsync ("Folder", SpecialFolder.All));

				// Rename - TODO

				// Append
				var international = FormatOptions.Default.Clone ();
				international.International = true;
				Assert.Throws<NotSupportedException> (() => inbox.Append (international, messages[0]));
				Assert.ThrowsAsync<NotSupportedException> (() => inbox.AppendAsync (international, messages[0]));
				Assert.Throws<NotSupportedException> (() => inbox.Append (international, messages[0], flags[0]));
				Assert.ThrowsAsync<NotSupportedException> (() => inbox.AppendAsync (international, messages[0], flags[0]));
				Assert.Throws<NotSupportedException> (() => inbox.Append (international, messages[0], flags[0], dates[0]));
				Assert.ThrowsAsync<NotSupportedException> (() => inbox.AppendAsync (international, messages[0], flags[0], dates[0]));

				// MultiAppend
				//Assert.Throws<NotSupportedException> (() => inbox.Append (international, messages));
				//Assert.ThrowsAsync<NotSupportedException> (() => inbox.AppendAsync (international, messages));
				Assert.Throws<NotSupportedException> (() => inbox.Append (international, messages, flags));
				Assert.ThrowsAsync<NotSupportedException> (() => inbox.AppendAsync (international, messages, flags));
				Assert.Throws<NotSupportedException> (() => inbox.Append (international, messages, flags, dates));
				Assert.ThrowsAsync<NotSupportedException> (() => inbox.AppendAsync (international, messages, flags, dates));

				// Status
				Assert.Throws<NotSupportedException> (() => inbox.Status (StatusItems.Count));
				Assert.ThrowsAsync<NotSupportedException> (() => inbox.StatusAsync (StatusItems.Count));

				// GetAccessControlList
				Assert.Throws<NotSupportedException> (() => inbox.GetAccessControlList ());
				Assert.ThrowsAsync<NotSupportedException> (() => inbox.GetAccessControlListAsync ());

				// GetAccessRights
				Assert.Throws<NotSupportedException> (() => inbox.GetAccessRights ("name"));
				Assert.ThrowsAsync<NotSupportedException> (() => inbox.GetAccessRightsAsync ("name"));

				// GetMyAccessRights
				Assert.Throws<NotSupportedException> (() => inbox.GetMyAccessRights ());
				Assert.ThrowsAsync<NotSupportedException> (() => inbox.GetMyAccessRightsAsync ());

				// RemoveAccess
				Assert.Throws<NotSupportedException> (() => inbox.RemoveAccess ("name"));
				Assert.ThrowsAsync<NotSupportedException> (() => inbox.RemoveAccessAsync ("name"));

				// GetMetadata
				Assert.Throws<NotSupportedException> (() => client.GetMetadata (MetadataTag.PrivateComment));
				Assert.ThrowsAsync<NotSupportedException> (() => client.GetMetadataAsync (MetadataTag.PrivateComment));
				Assert.Throws<NotSupportedException> (() => inbox.GetMetadata (MetadataTag.PrivateComment));
				Assert.ThrowsAsync<NotSupportedException> (() => inbox.GetMetadataAsync (MetadataTag.PrivateComment));
				Assert.Throws<NotSupportedException> (() => client.GetMetadata (new MetadataOptions (), new MetadataTag[] { MetadataTag.PrivateComment }));
				Assert.ThrowsAsync<NotSupportedException> (() => client.GetMetadataAsync (new MetadataOptions (), new MetadataTag[] { MetadataTag.PrivateComment }));
				Assert.Throws<NotSupportedException> (() => inbox.GetMetadata (new MetadataOptions (), new MetadataTag[] { MetadataTag.PrivateComment }));
				Assert.ThrowsAsync<NotSupportedException> (() => inbox.GetMetadataAsync (new MetadataOptions (), new MetadataTag[] { MetadataTag.PrivateComment }));

				// SetMetadata
				Assert.Throws<NotSupportedException> (() => client.SetMetadata (new MetadataCollection ()));
				Assert.ThrowsAsync<NotSupportedException> (() => client.SetMetadataAsync (new MetadataCollection ()));
				Assert.Throws<NotSupportedException> (() => inbox.SetMetadata (new MetadataCollection ()));
				Assert.ThrowsAsync<NotSupportedException> (() => inbox.SetMetadataAsync (new MetadataCollection ()));

				// GetQuota
				Assert.Throws<NotSupportedException> (() => inbox.GetQuota ());
				Assert.ThrowsAsync<NotSupportedException> (() => inbox.GetQuotaAsync ());

				// SetQuota
				Assert.Throws<NotSupportedException> (() => inbox.SetQuota (5, 10));
				Assert.ThrowsAsync<NotSupportedException> (() => inbox.SetQuotaAsync (5, 10));

				client.Disconnect (false);

				foreach (var message in messages)
					message.Dispose ();
			}
		}

		static IList<ImapReplayCommand> CreateLiteralFolderNamesCommands ()
		{
			return new List<ImapReplayCommand> {
				new ImapReplayCommand ("", "common.basic-greeting.txt"),
				new ImapReplayCommand ("A00000000 CAPABILITY\r\n", "common.capability.txt"),
				new ImapReplayCommand ("A00000001 LOGIN username password\r\n", ImapReplayCommandResponse.OK),
				new ImapReplayCommand ("A00000002 CAPABILITY\r\n", "common.capability.txt"),
				new ImapReplayCommand ("A00000003 LIST \"\" \"\"\r\n", "common.list-namespace.txt"),
				new ImapReplayCommand ("A00000004 LIST \"\" \"INBOX\"\r\n", "common.list-inbox.txt"),
				new ImapReplayCommand ("A00000005 LIST \"\" \"%\"\r\n", "common.list-literal-subfolders.txt"),
				new ImapReplayCommand ("A00000006 STATUS \"Literal Folder Name\" (MESSAGES)\r\n", "common.status-literal-folder.txt"),
			};
		}

		[Test]
		public void TestLiteralFolderNames ()
		{
			var commands = CreateLiteralFolderNamesCommands ();

			using (var client = new ImapClient () { TagPrefix = 'A' }) {
				try {
					client.Connect (new ImapReplayStream (commands, false), "localhost", 143, SecureSocketOptions.None);
				} catch (Exception ex) {
					Assert.Fail ($"Did not expect an exception in Connect: {ex}");
				}

				try {
					client.Authenticate ("username", "password");
				} catch (Exception ex) {
					Assert.Fail ($"Did not expect an exception in Authenticate: {ex}");
				}

				var personal = client.GetFolder (client.PersonalNamespaces[0]);
				var subfolders = personal.GetSubfolders (false);

				Assert.That (subfolders, Has.Count.EqualTo (2), "Count");
				Assert.That (subfolders[0].Name, Is.EqualTo ("INBOX"));
				Assert.That (subfolders[1].Name, Is.EqualTo ("Literal Folder Name"));

				subfolders[1].Status (StatusItems.Count);

				Assert.That (subfolders[1], Has.Count.EqualTo (60), "Count");

				client.Disconnect (false);
			}
		}

		[Test]
		public async Task TestLiteralFolderNamesAsync ()
		{
			var commands = CreateLiteralFolderNamesCommands ();

			using (var client = new ImapClient () { TagPrefix = 'A' }) {
				try {
					await client.ConnectAsync (new ImapReplayStream (commands, true), "localhost", 143, SecureSocketOptions.None);
				} catch (Exception ex) {
					Assert.Fail ($"Did not expect an exception in Connect: {ex}");
				}

				try {
					await client.AuthenticateAsync ("username", "password");
				} catch (Exception ex) {
					Assert.Fail ($"Did not expect an exception in Authenticate: {ex}");
				}

				var personal = client.GetFolder (client.PersonalNamespaces[0]);
				var subfolders = await personal.GetSubfoldersAsync (false);

				Assert.That (subfolders, Has.Count.EqualTo (2), "Count");
				Assert.That (subfolders[0].Name, Is.EqualTo ("INBOX"));
				Assert.That (subfolders[1].Name, Is.EqualTo ("Literal Folder Name"));

				await subfolders[1].StatusAsync (StatusItems.Count);

				Assert.That (subfolders[1], Has.Count.EqualTo (60), "Count");

				await client.DisconnectAsync (false);
			}
		}

		static IList<ImapReplayCommand> CreateNilDirectorySeparatorCommands ()
		{
			return new List<ImapReplayCommand> {
				new ImapReplayCommand ("", "common.basic-greeting.txt"),
				new ImapReplayCommand ("A00000000 CAPABILITY\r\n", "common.capability.txt"),
				new ImapReplayCommand ("A00000001 LOGIN username password\r\n", ImapReplayCommandResponse.OK),
				new ImapReplayCommand ("A00000002 CAPABILITY\r\n", "common.capability.txt"),
				new ImapReplayCommand ("A00000003 LIST \"\" \"\"\r\n", "common.list-namespace.txt"),
				new ImapReplayCommand ("A00000004 LIST \"\" \"INBOX\"\r\n", "common.list-inbox.txt"),
				new ImapReplayCommand ("A00000005 LIST \"\" \"%\"\r\n", "common.list-nil-folder-delim.txt"),
			};
		}

		[Test]
		public void TestNilDirectorySeparator ()
		{
			var commands = CreateNilDirectorySeparatorCommands ();

			using (var client = new ImapClient () { TagPrefix = 'A' }) {
				try {
					client.Connect (new ImapReplayStream (commands, false), "localhost", 143, SecureSocketOptions.None);
				} catch (Exception ex) {
					Assert.Fail ($"Did not expect an exception in Connect: {ex}");
				}

				try {
					client.Authenticate ("username", "password");
				} catch (Exception ex) {
					Assert.Fail ($"Did not expect an exception in Authenticate: {ex}");
				}

				var personal = client.GetFolder (client.PersonalNamespaces[0]);
				var subfolders = personal.GetSubfolders (false);

				Assert.That (subfolders, Has.Count.EqualTo (3), "Count");
				Assert.That (subfolders[0].Name, Is.EqualTo ("INBOX"));
				Assert.That (subfolders[1].Name, Is.EqualTo ("Folder1"));
				Assert.That (subfolders[1].DirectorySeparator, Is.EqualTo ('\0'));
				Assert.That (subfolders[2].Name, Is.EqualTo ("Folder2"));
				Assert.That (subfolders[2].DirectorySeparator, Is.EqualTo ('\0'));

				Assert.Throws<FolderNotFoundException> (() => subfolders[1].GetSubfolder ("Subfolder"));

				var empty = subfolders[1].GetSubfolders (false);
				Assert.That (empty, Is.Empty, "GetSubfolders when DirectorySeparator is nil");

				client.Disconnect (false);
			}
		}

		[Test]
		public async Task TestNilDirectorySeparatorAsync ()
		{
			var commands = CreateNilDirectorySeparatorCommands ();

			using (var client = new ImapClient () { TagPrefix = 'A' }) {
				try {
					await client.ConnectAsync (new ImapReplayStream (commands, true), "localhost", 143, SecureSocketOptions.None);
				} catch (Exception ex) {
					Assert.Fail ($"Did not expect an exception in Connect: {ex}");
				}

				try {
					await client.AuthenticateAsync ("username", "password");
				} catch (Exception ex) {
					Assert.Fail ($"Did not expect an exception in Authenticate: {ex}");
				}

				var personal = client.GetFolder (client.PersonalNamespaces[0]);
				var subfolders = await personal.GetSubfoldersAsync (false);

				Assert.That (subfolders, Has.Count.EqualTo (3), "Count");
				Assert.That (subfolders[0].Name, Is.EqualTo ("INBOX"));
				Assert.That (subfolders[1].Name, Is.EqualTo ("Folder1"));
				Assert.That (subfolders[1].DirectorySeparator, Is.EqualTo ('\0'));
				Assert.That (subfolders[2].Name, Is.EqualTo ("Folder2"));
				Assert.That (subfolders[2].DirectorySeparator, Is.EqualTo ('\0'));

				Assert.ThrowsAsync<FolderNotFoundException> (() => subfolders[1].GetSubfolderAsync ("Subfolder"));

				var empty = await subfolders[1].GetSubfoldersAsync (false);
				Assert.That (empty, Is.Empty, "GetSubfolders when DirectorySeparator is nil");

				await client.DisconnectAsync (false);
			}
		}

		static IList<ImapReplayCommand> CreateAppendLimitCommands ()
		{
			return new List<ImapReplayCommand> {
				new ImapReplayCommand ("", "gmail.greeting.txt"),
				new ImapReplayCommand ("A00000000 CAPABILITY\r\n", "gmail.capability.txt"),
				new ImapReplayCommand ("A00000001 AUTHENTICATE PLAIN AHVzZXJuYW1lAHBhc3N3b3Jk\r\n", "gmail.authenticate-no-appendlimit-value.txt"),
				new ImapReplayCommand ("A00000002 NAMESPACE\r\n", "gmail.namespace.txt"),
				new ImapReplayCommand ("A00000003 LIST \"\" \"INBOX\" RETURN (SUBSCRIBED CHILDREN)\r\n", "gmail.list-inbox.txt"),
				new ImapReplayCommand ("A00000004 XLIST \"\" \"*\"\r\n", "gmail.xlist.txt"),
				new ImapReplayCommand ("A00000005 STATUS INBOX (APPENDLIMIT)\r\n", "gmail.status-inbox-appendlimit.txt"),
				new ImapReplayCommand ("A00000006 STATUS INBOX (APPENDLIMIT)\r\n", "gmail.status-inbox-appendlimit-nil.txt"),
				new ImapReplayCommand ("A00000007 LIST \"\" \"%\" RETURN (SUBSCRIBED CHILDREN STATUS (MESSAGES UNSEEN APPENDLIMIT SIZE))\r\n", "gmail.list-personal-status-appendlimit.txt")
			};
		}

		[Test]
		public void TestAppendLimit ()
		{
			var commands = CreateAppendLimitCommands ();

			using (var client = new ImapClient () { TagPrefix = 'A' }) {
				try {
					client.Connect (new ImapReplayStream (commands, false), "localhost", 143, SecureSocketOptions.None);
				} catch (Exception ex) {
					Assert.Fail ($"Did not expect an exception in Connect: {ex}");
				}

				try {
					client.Authenticate ("username", "password");
				} catch (Exception ex) {
					Assert.Fail ($"Did not expect an exception in Authenticate: {ex}");
				}

				Assert.That (client.Capabilities.HasFlag (ImapCapabilities.AppendLimit), Is.True, "ImapCapabilities.AppendLimit");
				Assert.That (client.AppendLimit, Is.Null, "AppendLimit");

				client.Inbox.Status (StatusItems.AppendLimit);
				Assert.That (client.Inbox.AppendLimit, Is.EqualTo (35651584), "Inbox.AppendLimit");

				client.Inbox.Status (StatusItems.AppendLimit);
				Assert.That (client.Inbox.AppendLimit, Is.Null, "Inbox.AppendLimit NIL");

				var personal = client.GetFolder (client.PersonalNamespaces[0]);
				var subfolders = personal.GetSubfolders (StatusItems.Count | StatusItems.Unread | StatusItems.Size | StatusItems.AppendLimit, subscribedOnly: false);
				Assert.That (subfolders, Has.Count.EqualTo (2), "Count");
				Assert.That (subfolders[0].Name, Is.EqualTo ("INBOX"));
				Assert.That (subfolders[0].AppendLimit, Is.EqualTo (1234567890), "Inbox.AppendLimit");
				Assert.That (subfolders[0], Has.Count.EqualTo (10), "Inbox.Count");
				Assert.That (subfolders[0].Unread, Is.EqualTo (1), "Inbox.Unread");
				Assert.That (subfolders[0].Size, Is.EqualTo (123456789), "Inbox.Size");

				client.Disconnect (false);
			}
		}

		[Test]
		public async Task TestAppendLimitAsync ()
		{
			var commands = CreateAppendLimitCommands ();

			using (var client = new ImapClient () { TagPrefix = 'A' }) {
				try {
					await client.ConnectAsync (new ImapReplayStream (commands, true), "localhost", 143, SecureSocketOptions.None);
				} catch (Exception ex) {
					Assert.Fail ($"Did not expect an exception in Connect: {ex}");
				}

				try {
					await client.AuthenticateAsync ("username", "password");
				} catch (Exception ex) {
					Assert.Fail ($"Did not expect an exception in Authenticate: {ex}");
				}

				Assert.That (client.Capabilities.HasFlag (ImapCapabilities.AppendLimit), Is.True, "ImapCapabilities.AppendLimit");
				Assert.That (client.AppendLimit, Is.Null, "AppendLimit");

				await client.Inbox.StatusAsync (StatusItems.AppendLimit);
				Assert.That (client.Inbox.AppendLimit, Is.EqualTo (35651584), "Inbox.AppendLimit");

				await client.Inbox.StatusAsync (StatusItems.AppendLimit);
				Assert.That (client.Inbox.AppendLimit, Is.Null, "Inbox.AppendLimit NIL");

				var personal = client.GetFolder (client.PersonalNamespaces[0]);
				var subfolders = await personal.GetSubfoldersAsync (StatusItems.Count | StatusItems.Unread | StatusItems.Size | StatusItems.AppendLimit, subscribedOnly: false);
				Assert.That (subfolders, Has.Count.EqualTo (2), "Count");
				Assert.That (subfolders[0].Name, Is.EqualTo ("INBOX"));
				Assert.That (subfolders[0].AppendLimit, Is.EqualTo (1234567890), "Inbox.AppendLimit");
				Assert.That (subfolders[0], Has.Count.EqualTo (10), "Inbox.Count");
				Assert.That (subfolders[0].Unread, Is.EqualTo (1), "Inbox.Unread");
				Assert.That (subfolders[0].Size, Is.EqualTo (123456789), "Inbox.Size");

				await client.DisconnectAsync (false);
			}
		}

		static List<ImapReplayCommand> CreateAppendCommands (bool withKeywords, bool withInternalDates, out List<MimeMessage> messages, out List<MessageFlags> flags, out List<List<string>> keywords, out List<DateTimeOffset> internalDates)
		{
			var commands = new List<ImapReplayCommand> {
				new ImapReplayCommand ("", "gmail.greeting.txt"),
				new ImapReplayCommand ("A00000000 CAPABILITY\r\n", "gmail.capability.txt"),
				new ImapReplayCommand ("A00000001 AUTHENTICATE PLAIN AHVzZXJuYW1lAHBhc3N3b3Jk\r\n", "gmail.authenticate.txt"),
				new ImapReplayCommand ("A00000002 NAMESPACE\r\n", "gmail.namespace.txt"),
				new ImapReplayCommand ("A00000003 LIST \"\" \"INBOX\" RETURN (SUBSCRIBED CHILDREN)\r\n", "gmail.list-inbox.txt"),
				new ImapReplayCommand ("A00000004 XLIST \"\" \"*\"\r\n", "gmail.xlist.txt")
			};

			internalDates = withInternalDates ? new List<DateTimeOffset> () : null;
			keywords = withKeywords ? new List<List<string>> () : null;
			messages = new List<MimeMessage> ();
			flags = new List<MessageFlags> ();
			var command = new StringBuilder ();
			int id = 5;

			for (int i = 0; i < 8; i++) {
				MimeMessage message;
				string latin1;
				long length;

				using (var resource = GetResourceStream (string.Format ("common.message.{0}.msg", i)))
					message = MimeMessage.Load (resource);

				messages.Add (message);
				flags.Add (MessageFlags.Seen);

				if (withKeywords)
					keywords.Add (new List<string> { "$NotJunk" });

				if (withInternalDates)
					internalDates.Add (message.Date);

				using (var stream = new MemoryStream ()) {
					var options = FormatOptions.Default.Clone ();
					options.NewLineFormat = NewLineFormat.Dos;
					options.EnsureNewLine = true;

					message.WriteTo (options, stream);
					length = stream.Length;
					stream.Position = 0;

					using (var reader = new StreamReader (stream, Latin1))
						latin1 = reader.ReadToEnd ();
				}

				var tag = string.Format ("A{0:D8}", id++);
				command.Clear ();

				if (withKeywords)
					command.AppendFormat ("{0} APPEND INBOX (\\Seen $NotJunk) ", tag);
				else
					command.AppendFormat ("{0} APPEND INBOX (\\Seen) ", tag);

				if (withInternalDates)
					command.AppendFormat ("\"{0}\" ", ImapUtils.FormatInternalDate (message.Date));

				if (length > 4096) {
					command.Append ('{').Append (length.ToString (CultureInfo.InvariantCulture)).Append ("}\r\n");
					commands.Add (new ImapReplayCommand (command.ToString (), ImapReplayCommandResponse.Plus));
					commands.Add (new ImapReplayCommand (tag, latin1 + "\r\n", string.Format ("dovecot.append.{0}.txt", i + 1)));
				} else {
					command.Append ('{').Append (length.ToString (CultureInfo.InvariantCulture)).Append ("+}\r\n").Append (latin1).Append ("\r\n");
					commands.Add (new ImapReplayCommand (command.ToString (), string.Format ("dovecot.append.{0}.txt", i + 1)));
				}
			}

			commands.Add (new ImapReplayCommand (string.Format ("A{0:D8} LOGOUT\r\n", id), "gmail.logout.txt"));

			return commands;
		}

		[TestCase (false, false, TestName = "TestAppend")]
		[TestCase (true, false, TestName = "TestAppendWithKeywords")]
		[TestCase (false, true, TestName = "TestAppendWithInternalDates")]
		[TestCase (true, true, TestName = "TestAppendWithKeywordsAndInternalDates")]
		public void TestAppend (bool withKeywords, bool withInternalDates)
		{
			var commands = CreateAppendCommands (withKeywords, withInternalDates, out var messages, out var flags, out var keywords, out var internalDates);

			using (var client = new ImapClient () { TagPrefix = 'A' }) {
				try {
					client.Connect (new ImapReplayStream (commands, false), "localhost", 143, SecureSocketOptions.None);
				} catch (Exception ex) {
					Assert.Fail ($"Did not expect an exception in Connect: {ex}");
				}

				try {
					client.Authenticate ("username", "password");
				} catch (Exception ex) {
					Assert.Fail ($"Did not expect an exception in Authenticate: {ex}");
				}

				for (int i = 0; i < messages.Count; i++) {
					UniqueId? uid;

					if (withKeywords) {
						AppendRequest request;

						if (withInternalDates) {
							request = new AppendRequest (messages[i], flags[i], keywords[i], internalDates[i]);
						} else {
							request = new AppendRequest (messages[i], flags[i], keywords[i]);
						}

						uid = client.Inbox.Append (request);
					} else if (withInternalDates) {
						uid = client.Inbox.Append (messages[i], flags[i], internalDates[i]);
					} else {
						uid = client.Inbox.Append (messages[i], flags[i]);
					}

					Assert.That (uid.HasValue, Is.True, "Expected a UIDAPPEND resp-code");
					Assert.That (uid.Value.Id, Is.EqualTo (i + 1), "Unexpected UID");

					messages[i].Dispose ();
				}

				client.Disconnect (true);
			}
		}

		[TestCase (false, false, TestName = "TestAppendAsync")]
		[TestCase (true, false, TestName = "TestAppendWithKeywordsAsync")]
		[TestCase (false, true, TestName = "TestAppendWithInternalDatesAsync")]
		[TestCase (true, true, TestName = "TestAppendWithKeywordsAndInternalDatesAsync")]
		public async Task TestAppendAsync (bool withKeywords, bool withInternalDates)
		{
			var commands = CreateAppendCommands (withKeywords, withInternalDates, out var messages, out var flags, out var keywords, out var internalDates);

			using (var client = new ImapClient () { TagPrefix = 'A' }) {
				try {
					await client.ConnectAsync (new ImapReplayStream (commands, true), "localhost", 143, SecureSocketOptions.None);
				} catch (Exception ex) {
					Assert.Fail ($"Did not expect an exception in Connect: {ex}");
				}

				try {
					await client.AuthenticateAsync ("username", "password");
				} catch (Exception ex) {
					Assert.Fail ($"Did not expect an exception in Authenticate: {ex}");
				}

				for (int i = 0; i < messages.Count; i++) {
					UniqueId? uid;

					if (withKeywords) {
						AppendRequest request;

						if (withInternalDates) {
							request = new AppendRequest (messages[i], flags[i], keywords[i], internalDates[i]);
						} else {
							request = new AppendRequest (messages[i], flags[i], keywords[i]);
						}

						uid = await client.Inbox.AppendAsync (request);
					} else if (withInternalDates) {
						uid = await client.Inbox.AppendAsync (messages[i], flags[i], internalDates[i]);
					} else {
						uid = await client.Inbox.AppendAsync (messages[i], flags[i]);
					}

					Assert.That (uid.HasValue, Is.True, "Expected a UIDAPPEND resp-code");
					Assert.That (uid.Value.Id, Is.EqualTo (i + 1), "Unexpected UID");

					messages[i].Dispose ();
				}

				await client.DisconnectAsync (true);
			}
		}

		static List<ImapReplayCommand> CreateMultiAppendCommands (bool withKeywords, bool withInternalDates, out List<MimeMessage> messages, out List<MessageFlags> flags, out List<List<string>> keywords, out List<DateTimeOffset> internalDates)
		{
			var commands = new List<ImapReplayCommand> {
				new ImapReplayCommand ("", "dovecot.greeting.txt"),
				new ImapReplayCommand ("A00000000 LOGIN username password\r\n", "dovecot.authenticate.txt"),
				new ImapReplayCommand ("A00000001 NAMESPACE\r\n", "dovecot.namespace.txt"),
				new ImapReplayCommand ("A00000002 LIST \"\" \"INBOX\" RETURN (SUBSCRIBED CHILDREN)\r\n", "dovecot.list-inbox.txt"),
				new ImapReplayCommand ("A00000003 LIST (SPECIAL-USE) \"\" \"*\" RETURN (SUBSCRIBED CHILDREN)\r\n", "dovecot.list-special-use.txt")
			};

			var command = new StringBuilder ("A00000004 APPEND INBOX");
			var now = DateTimeOffset.Now;

			internalDates = withInternalDates ? new List<DateTimeOffset> () : null;
			keywords = withKeywords ? new List<List<string>> () : null;
			messages = new List<MimeMessage> ();
			flags = new List<MessageFlags> ();

			messages.Add (CreateThreadableMessage ("A", "<a@mimekit.net>", null, now.AddMinutes (-7)));
			messages.Add (CreateThreadableMessage ("B", "<b@mimekit.net>", "<a@mimekit.net>", now.AddMinutes (-6)));
			messages.Add (CreateThreadableMessage ("C", "<c@mimekit.net>", "<a@mimekit.net> <b@mimekit.net>", now.AddMinutes (-5)));
			messages.Add (CreateThreadableMessage ("D", "<d@mimekit.net>", "<a@mimekit.net>", now.AddMinutes (-4)));
			messages.Add (CreateThreadableMessage ("E", "<e@mimekit.net>", "<c@mimekit.net> <x@mimekit.net> <y@mimekit.net> <z@mimekit.net>", now.AddMinutes (-3)));
			messages.Add (CreateThreadableMessage ("F", "<f@mimekit.net>", "<b@mimekit.net>", now.AddMinutes (-2)));
			messages.Add (CreateThreadableMessage ("G", "<g@mimekit.net>", null, now.AddMinutes (-1)));
			messages.Add (CreateThreadableMessage ("H", "<h@mimekit.net>", null, now));

			for (int i = 0; i < messages.Count; i++) {
				var message = messages[i];
				string latin1;
				long length;

				flags.Add (MessageFlags.Seen);

				if (withKeywords)
					keywords.Add (new List<string> { "$NotJunk" });

				if (withInternalDates)
					internalDates.Add (messages[i].Date);

				using (var stream = new MemoryStream ()) {
					var options = FormatOptions.Default.Clone ();
					options.NewLineFormat = NewLineFormat.Dos;

					message.WriteTo (options, stream);
					length = stream.Length;
					stream.Position = 0;

					using (var reader = new StreamReader (stream, Latin1))
						latin1 = reader.ReadToEnd ();
				}

				if (withKeywords)
					command.Append (" (\\Seen $NotJunk) ");
				else
					command.Append (" (\\Seen) ");

				if (withInternalDates)
					command.AppendFormat ("\"{0}\" ", ImapUtils.FormatInternalDate (message.Date));

				command.Append ('{');
				command.AppendFormat ("{0}+", length);
				command.Append ("}\r\n");
				command.Append (latin1);
			}
			command.Append ("\r\n");
			commands.Add (new ImapReplayCommand (command.ToString (), "dovecot.multiappend.txt"));

			for (int i = 0; i < messages.Count; i++) {
				var message = messages[i];
				string latin1;
				long length;

				command.Clear ();
				command.AppendFormat ("A{0:D8} APPEND INBOX", i + 5);

				using (var stream = new MemoryStream ()) {
					var options = FormatOptions.Default.Clone ();
					options.NewLineFormat = NewLineFormat.Dos;

					message.WriteTo (options, stream);
					length = stream.Length;
					stream.Position = 0;

					using (var reader = new StreamReader (stream, Latin1))
						latin1 = reader.ReadToEnd ();
				}

				if (withKeywords)
					command.Append (" (\\Seen $NotJunk) ");
				else
					command.Append (" (\\Seen) ");

				if (withInternalDates)
					command.AppendFormat ("\"{0}\" ", ImapUtils.FormatInternalDate (message.Date));

				command.Append ('{');
				command.AppendFormat ("{0}+", length);
				command.Append ("}\r\n");
				command.Append (latin1);
				command.Append ("\r\n");
				commands.Add (new ImapReplayCommand (command.ToString (), string.Format ("dovecot.append.{0}.txt", i + 1)));
			}

			commands.Add (new ImapReplayCommand ("A00000013 LOGOUT\r\n", "gmail.logout.txt"));

			return commands;
		}

		[TestCase (false, false, TestName = "TestMultiAppend")]
		[TestCase (true, false, TestName = "TestMultiAppendWithKeywords")]
		[TestCase (false, true, TestName = "TestMultiAppendWithInternalDates")]
		[TestCase (true, true, TestName = "TestMultiAppendWithKeywordsAndInternalDates")]
		public void TestMultiAppend (bool withKeywords, bool withInternalDates)
		{
			var commands = CreateMultiAppendCommands (withKeywords, withInternalDates, out var messages, out var flags, out var keywords, out var internalDates);
			IList<UniqueId> uids;

			using (var client = new ImapClient () { TagPrefix = 'A' }) {
				try {
					client.Connect (new ImapReplayStream (commands, false), "localhost", 143, SecureSocketOptions.None);
				} catch (Exception ex) {
					Assert.Fail ($"Did not expect an exception in Connect: {ex}");
				}

				// Note: we do not want to use SASL at all...
				client.AuthenticationMechanisms.Clear ();

				try {
					client.Authenticate ("username", "password");
				} catch (Exception ex) {
					Assert.Fail ($"Did not expect an exception in Authenticate: {ex}");
				}

				Assert.That (client.Capabilities.HasFlag (ImapCapabilities.MultiAppend), Is.True, "MULTIAPPEND");

				// Use MULTIAPPEND to append some test messages
				if (withKeywords) {
					var requests = new List<IAppendRequest> ();

					for (int i = 0; i < messages.Count; i++) {
						if (withInternalDates) {
							requests.Add (new AppendRequest (messages[i], flags[i], keywords[i], internalDates[i]));
						} else {
							requests.Add (new AppendRequest (messages[i], flags[i], keywords[i]));
						}
					}

					uids = client.Inbox.Append (requests);
				} else if (withInternalDates) {
					uids = client.Inbox.Append (messages, flags, internalDates);
				} else {
					uids = client.Inbox.Append (messages, flags);
				}

				Assert.That (uids, Has.Count.EqualTo (8), "Unexpected number of messages appended");

				for (int i = 0; i < uids.Count; i++)
					Assert.That (uids[i].Id, Is.EqualTo (i + 1), "Unexpected UID");

				// Disable the MULTIAPPEND extension and do it again
				client.Capabilities &= ~ImapCapabilities.MultiAppend;

				if (withKeywords) {
					var requests = new List<IAppendRequest> ();

					for (int i = 0; i < messages.Count; i++) {
						if (withInternalDates) {
							requests.Add (new AppendRequest (messages[i], flags[i], keywords[i], internalDates[i]));
						} else {
							requests.Add (new AppendRequest (messages[i], flags[i], keywords[i]));
						}
					}

					uids = client.Inbox.Append (requests);
				} else if (withInternalDates) {
					uids = client.Inbox.Append (messages, flags, internalDates);
				} else {
					uids = client.Inbox.Append (messages, flags);
				}

				Assert.That (uids, Has.Count.EqualTo (8), "Unexpected number of messages appended");

				for (int i = 0; i < uids.Count; i++)
					Assert.That (uids[i].Id, Is.EqualTo (i + 1), "Unexpected UID");

				client.Disconnect (true);

				foreach (var message in messages)
					message.Dispose ();
			}
		}

		[TestCase (false, false, TestName = "TestMultiAppendAsync")]
		[TestCase (true, false, TestName = "TestMultiAppendWithKeywordsAsync")]
		[TestCase (false, true, TestName = "TestMultiAppendWithInternalDatesAsync")]
		[TestCase (true, true, TestName = "TestMultiAppendWithKeywordsAndInternalDatesAsync")]
		public async Task TestMultiAppendAsync (bool withKeywords, bool withInternalDates)
		{
			var commands = CreateMultiAppendCommands (withKeywords, withInternalDates, out var messages, out var flags, out var keywords, out var internalDates);
			IList<UniqueId> uids;

			using (var client = new ImapClient () { TagPrefix = 'A' }) {
				try {
					await client.ConnectAsync (new ImapReplayStream (commands, true), "localhost", 143, SecureSocketOptions.None);
				} catch (Exception ex) {
					Assert.Fail ($"Did not expect an exception in Connect: {ex}");
				}

				// Note: we do not want to use SASL at all...
				client.AuthenticationMechanisms.Clear ();

				try {
					await client.AuthenticateAsync ("username", "password");
				} catch (Exception ex) {
					Assert.Fail ($"Did not expect an exception in Authenticate: {ex}");
				}

				Assert.That (client.Capabilities.HasFlag (ImapCapabilities.MultiAppend), Is.True, "MULTIAPPEND");

				// Use MULTIAPPEND to append some test messages
				if (withKeywords) {
					var requests = new List<IAppendRequest> ();

					for (int i = 0; i < messages.Count; i++) {
						if (withInternalDates) {
							requests.Add (new AppendRequest (messages[i], flags[i], keywords[i], internalDates[i]));
						} else {
							requests.Add (new AppendRequest (messages[i], flags[i], keywords[i]));
						}
					}

					uids = await client.Inbox.AppendAsync (requests);
				} else if (withInternalDates) {
					uids = await client.Inbox.AppendAsync (messages, flags, internalDates);
				} else {
					uids = await client.Inbox.AppendAsync (messages, flags);
				}

				Assert.That (uids, Has.Count.EqualTo (8), "Unexpected number of messages appended");

				for (int i = 0; i < uids.Count; i++)
					Assert.That (uids[i].Id, Is.EqualTo (i + 1), "Unexpected UID");

				// Disable the MULTIAPPEND extension and do it again
				client.Capabilities &= ~ImapCapabilities.MultiAppend;

				if (withKeywords) {
					var requests = new List<IAppendRequest> ();

					for (int i = 0; i < messages.Count; i++) {
						if (withInternalDates) {
							requests.Add (new AppendRequest (messages[i], flags[i], keywords[i], internalDates[i]));
						} else {
							requests.Add (new AppendRequest (messages[i], flags[i], keywords[i]));
						}
					}

					uids = await client.Inbox.AppendAsync (requests);
				} else if (withInternalDates) {
					uids = await client.Inbox.AppendAsync (messages, flags, internalDates);
				} else {
					uids = await client.Inbox.AppendAsync (messages, flags);
				}

				Assert.That (uids, Has.Count.EqualTo (8), "Unexpected number of messages appended");

				for (int i = 0; i < uids.Count; i++)
					Assert.That (uids[i].Id, Is.EqualTo (i + 1), "Unexpected UID");

				await client.DisconnectAsync (true);

				foreach (var message in messages)
					message.Dispose ();
			}
		}

		static List<ImapReplayCommand> CreateReplaceCommands (bool clientSide, bool withKeywords, bool withInternalDates, out List<MimeMessage> messages, out List<MessageFlags> flags, out List<List<string>> keywords, out List<DateTimeOffset> internalDates)
		{
			var commands = new List<ImapReplayCommand> {
				new ImapReplayCommand ("", "dovecot.greeting.txt"),
				new ImapReplayCommand ("A00000000 LOGIN username password\r\n", "dovecot.authenticate+replace.txt"),
				new ImapReplayCommand ("A00000001 NAMESPACE\r\n", "dovecot.namespace.txt"),
				new ImapReplayCommand ("A00000002 LIST \"\" \"INBOX\" RETURN (SUBSCRIBED CHILDREN)\r\n", "dovecot.list-inbox.txt"),
				new ImapReplayCommand ("A00000003 LIST (SPECIAL-USE) \"\" \"*\" RETURN (SUBSCRIBED CHILDREN)\r\n", "dovecot.list-special-use.txt"),
				new ImapReplayCommand ("A00000004 SELECT INBOX (CONDSTORE)\r\n", "common.select-inbox.txt")
			};

			internalDates = withInternalDates ? new List<DateTimeOffset> () : null;
			keywords = withKeywords ? new List<List<string>> () : null;
			messages = new List<MimeMessage> ();
			flags = new List<MessageFlags> ();
			var command = new StringBuilder ();
			int id = 5;

			for (int i = 0; i < 8; i++) {
				MimeMessage message;
				string latin1;
				long length;

				using (var resource = GetResourceStream (string.Format ("common.message.{0}.msg", i)))
					message = MimeMessage.Load (resource);

				messages.Add (message);

				flags.Add (MessageFlags.Seen);

				if (withKeywords)
					keywords.Add (new List<string> { "$NotJunk" });

				if (withInternalDates)
					internalDates.Add (message.Date);

				using (var stream = new MemoryStream ()) {
					var options = FormatOptions.Default.Clone ();
					options.NewLineFormat = NewLineFormat.Dos;
					options.EnsureNewLine = true;

					message.WriteTo (options, stream);
					length = stream.Length;
					stream.Position = 0;

					using (var reader = new StreamReader (stream, Latin1))
						latin1 = reader.ReadToEnd ();
				}

				var tag = string.Format ("A{0:D8}", id++);
				command.Clear ();

				if (clientSide)
					command.AppendFormat ("{0} APPEND INBOX (\\Seen", tag);
				else
					command.AppendFormat ("{0} REPLACE {1} INBOX (\\Seen", tag, i + 1);

				if (withKeywords)
					command.Append (" $NotJunk) ");
				else
					command.Append (") ");

				if (withInternalDates)
					command.AppendFormat ("\"{0}\" ", ImapUtils.FormatInternalDate (message.Date));

				//if (length > 4096) {
				//	command.Append ('{').Append (length.ToString ()).Append ("}\r\n");
				//	commands.Add (new ImapReplayCommand (command.ToString (), ImapReplayCommandResponse.Plus));
				//	commands.Add (new ImapReplayCommand (tag, latin1 + "\r\n", string.Format ("dovecot.append.{0}.txt", i + 1)));
				//} else {
					command.Append ('{').Append (length.ToString (CultureInfo.InvariantCulture)).Append ("+}\r\n").Append (latin1).Append ("\r\n");
					commands.Add (new ImapReplayCommand (command.ToString (), string.Format ("dovecot.append.{0}.txt", i + 1)));
				//}

				if (clientSide) {
					tag = string.Format ("A{0:D8}", id++);
					commands.Add (new ImapReplayCommand ($"{tag} STORE {i + 1} +FLAGS.SILENT (\\Deleted)\r\n", ImapReplayCommandResponse.OK));
				}
			}

			commands.Add (new ImapReplayCommand (string.Format ("A{0:D8} LOGOUT\r\n", id), "gmail.logout.txt"));

			return commands;
		}

		[TestCase (false, false, false, TestName = "TestReplace")]
		[TestCase (false, true, false, TestName = "TestReplaceWithKeywords")]
		[TestCase (false, false, true, TestName = "TestReplaceWithInternalDates")]
		[TestCase (false, true, true, TestName = "TestReplaceWithKeywordsAndInternalDates")]
		[TestCase (true, false, false, TestName = "TestClientSideReplace")]
		[TestCase (true, true, false, TestName = "TestClientSideReplaceWithKeywords")]
		[TestCase (true, false, true, TestName = "TestClientSideReplaceWithInternalDates")]
		[TestCase (true, true, true, TestName = "TestClientSideReplaceWithKeywordsAndInternalDates")]
		public void TestReplace (bool clientSide, bool withKeywords, bool withInternalDates)
		{
			var commands = CreateReplaceCommands (clientSide, withKeywords, withInternalDates, out var messages, out var flags, out var keywords, out var internalDates);

			using (var client = new ImapClient () { TagPrefix = 'A' }) {
				try {
					client.Connect (new ImapReplayStream (commands, false), "localhost", 143, SecureSocketOptions.None);
				} catch (Exception ex) {
					Assert.Fail ($"Did not expect an exception in Connect: {ex}");
				}

				// Note: we do not want to use SASL at all...
				client.AuthenticationMechanisms.Clear ();

				try {
					client.Authenticate ("username", "password");
				} catch (Exception ex) {
					Assert.Fail ($"Did not expect an exception in Authenticate: {ex}");
				}

				if (clientSide)
					client.Capabilities &= ~ImapCapabilities.Replace;
				else
					Assert.That (client.Capabilities.HasFlag (ImapCapabilities.Replace), Is.True, "REPLACE");

				client.Inbox.Open (FolderAccess.ReadWrite);

				for (int i = 0; i < messages.Count; i++) {
					UniqueId? uid;

					if (withKeywords) {
						ReplaceRequest request;

						if (withInternalDates) {
							request = new ReplaceRequest (messages[i], flags[i], keywords[i], internalDates[i]);
						} else {
							request = new ReplaceRequest (messages[i], flags[i], keywords[i]);
						}

						uid = client.Inbox.Replace (i, request);
					} else if (withInternalDates) {
						uid = client.Inbox.Replace (i, messages[i], flags[i], internalDates[i]);
					} else {
						uid = client.Inbox.Replace (i, messages[i], flags[i]);
					}

					Assert.That (uid.HasValue, Is.True, "Expected a UIDAPPEND resp-code");
					Assert.That (uid.Value.Id, Is.EqualTo (i + 1), "Unexpected UID");
				}

				client.Disconnect (true);

				foreach (var message in messages)
					message.Dispose ();
			}
		}

		[TestCase (false, false, false, TestName = "TestReplaceAsync")]
		[TestCase (false, true, false, TestName = "TestReplaceWithKeywordsAsync")]
		[TestCase (false, false, true, TestName = "TestReplaceWithInternalDatesAsync")]
		[TestCase (false, true, true, TestName = "TestReplaceWithKeywordsAndInternalDatesAsync")]
		[TestCase (true, false, false, TestName = "TestClientSideReplaceAsync")]
		[TestCase (true, true, false, TestName = "TestClientSideReplaceWithKeywordsAsync")]
		[TestCase (true, false, true, TestName = "TestClientSideReplaceWithInternalDatesAsync")]
		[TestCase (true, true, true, TestName = "TestClientSideReplaceWithKeywordsAndInternalDatesAsync")]
		public async Task TestReplaceAsync (bool clientSide, bool withKeywords, bool withInternalDates)
		{
			var commands = CreateReplaceCommands (clientSide, withKeywords, withInternalDates, out var messages, out var flags, out var keywords, out var internalDates);

			using (var client = new ImapClient () { TagPrefix = 'A' }) {
				try {
					await client.ConnectAsync (new ImapReplayStream (commands, true), "localhost", 143, SecureSocketOptions.None);
				} catch (Exception ex) {
					Assert.Fail ($"Did not expect an exception in Connect: {ex}");
				}

				// Note: we do not want to use SASL at all...
				client.AuthenticationMechanisms.Clear ();

				try {
					await client.AuthenticateAsync ("username", "password");
				} catch (Exception ex) {
					Assert.Fail ($"Did not expect an exception in Authenticate: {ex}");
				}

				if (clientSide)
					client.Capabilities &= ~ImapCapabilities.Replace;
				else
					Assert.That (client.Capabilities.HasFlag (ImapCapabilities.Replace), Is.True, "REPLACE");

				await client.Inbox.OpenAsync (FolderAccess.ReadWrite);

				for (int i = 0; i < messages.Count; i++) {
					UniqueId? uid;

					if (withKeywords) {
						ReplaceRequest request;

						if (withInternalDates) {
							request = new ReplaceRequest (messages[i], flags[i], keywords[i], internalDates[i]);
						} else {
							request = new ReplaceRequest (messages[i], flags[i], keywords[i]);
						}

						uid = await client.Inbox.ReplaceAsync (i, request);
					} else if (withInternalDates) {
						uid = await client.Inbox.ReplaceAsync (i, messages[i], flags[i], internalDates[i]);
					} else {
						uid = await client.Inbox.ReplaceAsync (i, messages[i], flags[i]);
					}

					Assert.That (uid.HasValue, Is.True, "Expected a UIDAPPEND resp-code");
					Assert.That (uid.Value.Id, Is.EqualTo (i + 1), "Unexpected UID");
				}

				await client.DisconnectAsync (true);

				foreach (var message in messages)
					message.Dispose ();
			}
		}

		static List<ImapReplayCommand> CreateReplaceByUidCommands (bool clientSide, bool withKeywords, bool withInternalDates, out List<MimeMessage> messages, out List<MessageFlags> flags, out List<List<string>> keywords, out List<DateTimeOffset> internalDates)
		{
			var commands = new List<ImapReplayCommand> {
				new ImapReplayCommand ("", "dovecot.greeting.txt"),
				new ImapReplayCommand ("A00000000 LOGIN username password\r\n", "dovecot.authenticate+replace.txt"),
				new ImapReplayCommand ("A00000001 NAMESPACE\r\n", "dovecot.namespace.txt"),
				new ImapReplayCommand ("A00000002 LIST \"\" \"INBOX\" RETURN (SUBSCRIBED CHILDREN)\r\n", "dovecot.list-inbox.txt"),
				new ImapReplayCommand ("A00000003 LIST (SPECIAL-USE) \"\" \"*\" RETURN (SUBSCRIBED CHILDREN)\r\n", "dovecot.list-special-use.txt"),
				new ImapReplayCommand ("A00000004 SELECT INBOX (CONDSTORE)\r\n", "common.select-inbox.txt")
			};

			internalDates = withInternalDates ? new List<DateTimeOffset> () : null;
			keywords = withKeywords ? new List<List<string>> () : null;
			messages = new List<MimeMessage> ();
			flags = new List<MessageFlags> ();
			var command = new StringBuilder ();
			int id = 5;

			for (int i = 0; i < 8; i++) {
				MimeMessage message;
				string latin1;
				long length;

				using (var resource = GetResourceStream (string.Format ("common.message.{0}.msg", i)))
					message = MimeMessage.Load (resource);

				messages.Add (message);

				flags.Add (MessageFlags.Seen);

				if (withKeywords)
					keywords.Add (new List<string> { "$NotJunk" });

				if (withInternalDates)
					internalDates.Add (message.Date);

				using (var stream = new MemoryStream ()) {
					var options = FormatOptions.Default.Clone ();
					options.NewLineFormat = NewLineFormat.Dos;
					options.EnsureNewLine = true;

					message.WriteTo (options, stream);
					length = stream.Length;
					stream.Position = 0;

					using (var reader = new StreamReader (stream, Latin1))
						latin1 = reader.ReadToEnd ();
				}

				var tag = string.Format ("A{0:D8}", id++);
				command.Clear ();

				if (clientSide)
					command.AppendFormat ("{0} APPEND INBOX (\\Seen", tag);
				else
					command.AppendFormat ("{0} UID REPLACE {1} INBOX (\\Seen", tag, i + 1);

				if (withKeywords)
					command.Append (" $NotJunk) ");
				else
					command.Append (") ");

				if (withInternalDates)
					command.AppendFormat ("\"{0}\" ", ImapUtils.FormatInternalDate (message.Date));

				//if (length > 4096) {
				//	command.Append ('{').Append (length.ToString ()).Append ("}\r\n");
				//	commands.Add (new ImapReplayCommand (command.ToString (), ImapReplayCommandResponse.Plus));
				//	commands.Add (new ImapReplayCommand (tag, latin1 + "\r\n", string.Format ("dovecot.append.{0}.txt", i + 1)));
				//} else {
				command.Append ('{').Append (length.ToString (CultureInfo.InvariantCulture)).Append ("+}\r\n").Append (latin1).Append ("\r\n");
				commands.Add (new ImapReplayCommand (command.ToString (), string.Format ("dovecot.append.{0}.txt", i + 1)));
				//}

				if (clientSide) {
					tag = string.Format ("A{0:D8}", id++);
					commands.Add (new ImapReplayCommand ($"{tag} UID STORE {i + 1} +FLAGS.SILENT (\\Deleted)\r\n", ImapReplayCommandResponse.OK));

					tag = string.Format ("A{0:D8}", id++);
					commands.Add (new ImapReplayCommand ($"{tag} UID EXPUNGE {i + 1}\r\n", ImapReplayCommandResponse.OK));
				}
			}

			commands.Add (new ImapReplayCommand (string.Format ("A{0:D8} LOGOUT\r\n", id), "gmail.logout.txt"));

			return commands;
		}

		[TestCase (false, false, false, TestName = "TestReplaceByUid")]
		[TestCase (false, true, false, TestName = "TestReplaceByUidWithKeywords")]
		[TestCase (false, false, true, TestName = "TestReplaceByUidWithInternalDates")]
		[TestCase (false, true, true, TestName = "TestReplaceByUidWithKeywordsAndInternalDates")]
		[TestCase (true, false, false, TestName = "TestClientSideReplaceByUid")]
		[TestCase (true, true, false, TestName = "TestClientSideReplaceByUidWithKeywords")]
		[TestCase (true, false, true, TestName = "TestClientSideReplaceByUidWithInternalDates")]
		[TestCase (true, true, true, TestName = "TestClientSideReplaceByUidWithKeywordsAndInternalDates")]
		public void TestReplaceByUid (bool clientSide, bool withKeywords, bool withInternalDates)
		{
			var commands = CreateReplaceByUidCommands (clientSide, withKeywords, withInternalDates, out var messages, out var flags, out var keywords, out var internalDates);

			using (var client = new ImapClient () { TagPrefix = 'A' }) {
				try {
					client.Connect (new ImapReplayStream (commands, false), "localhost", 143, SecureSocketOptions.None);
				} catch (Exception ex) {
					Assert.Fail ($"Did not expect an exception in Connect: {ex}");
				}

				// Note: we do not want to use SASL at all...
				client.AuthenticationMechanisms.Clear ();

				try {
					client.Authenticate ("username", "password");
				} catch (Exception ex) {
					Assert.Fail ($"Did not expect an exception in Authenticate: {ex}");
				}

				if (clientSide)
					client.Capabilities &= ~ImapCapabilities.Replace;
				else
					Assert.That (client.Capabilities.HasFlag (ImapCapabilities.Replace), Is.True, "REPLACE");

				client.Inbox.Open (FolderAccess.ReadWrite);

				for (int i = 0; i < messages.Count; i++) {
					UniqueId? uid;

					if (withKeywords) {
						ReplaceRequest request;

						if (withInternalDates) {
							request = new ReplaceRequest (messages[i], flags[i], keywords[i], internalDates[i]);
						} else {
							request = new ReplaceRequest (messages[i], flags[i], keywords[i]);
						}

						uid = client.Inbox.Replace (new UniqueId ((uint) i + 1), request);
					} else if (withInternalDates) {
						uid = client.Inbox.Replace (new UniqueId ((uint) i + 1), messages[i], flags[i], internalDates[i]);
					} else {
						uid = client.Inbox.Replace (new UniqueId ((uint) i + 1), messages[i], flags[i]);
					}

					Assert.That (uid.HasValue, Is.True, "Expected a UIDAPPEND resp-code");
					Assert.That (uid.Value.Id, Is.EqualTo (i + 1), "Unexpected UID");
				}

				client.Disconnect (true);

				foreach (var message in messages)
					message.Dispose ();
			}
		}

		[TestCase (false, false, false, TestName = "TestReplaceByUidAsync")]
		[TestCase (false, true, false, TestName = "TestReplaceByUidWithKeywordsAsync")]
		[TestCase (false, false, true, TestName = "TestReplaceByUidWithInternalDatesAsync")]
		[TestCase (false, true, true, TestName = "TestReplaceByUidWithKeywordsAndInternalDatesAsync")]
		[TestCase (true, false, false, TestName = "TestClientSideReplaceByUidAsync")]
		[TestCase (true, true, false, TestName = "TestClientSideReplaceByUidWithKeywordsAsync")]
		[TestCase (true, false, true, TestName = "TestClientSideReplaceByUidWithInternalDatesAsync")]
		[TestCase (true, true, true, TestName = "TestClientSideReplaceByUidWithKeywordsAndInternalDatesAsync")]
		public async Task TestReplaceByUidAsync (bool clientSide, bool withKeywords, bool withInternalDates)
		{
			var commands = CreateReplaceByUidCommands (clientSide, withKeywords, withInternalDates, out var messages, out var flags, out var keywords, out var internalDates);

			using (var client = new ImapClient () { TagPrefix = 'A' }) {
				try {
					await client.ConnectAsync (new ImapReplayStream (commands, true), "localhost", 143, SecureSocketOptions.None);
				} catch (Exception ex) {
					Assert.Fail ($"Did not expect an exception in Connect: {ex}");
				}

				// Note: we do not want to use SASL at all...
				client.AuthenticationMechanisms.Clear ();

				try {
					await client.AuthenticateAsync ("username", "password");
				} catch (Exception ex) {
					Assert.Fail ($"Did not expect an exception in Authenticate: {ex}");
				}

				if (clientSide)
					client.Capabilities &= ~ImapCapabilities.Replace;
				else
					Assert.That (client.Capabilities.HasFlag (ImapCapabilities.Replace), Is.True, "REPLACE");

				await client.Inbox.OpenAsync (FolderAccess.ReadWrite);

				for (int i = 0; i < messages.Count; i++) {
					UniqueId? uid;

					if (withKeywords) {
						ReplaceRequest request;

						if (withInternalDates) {
							request = new ReplaceRequest (messages[i], flags[i], keywords[i], internalDates[i]);
						} else {
							request = new ReplaceRequest (messages[i], flags[i], keywords[i]);
						}

						uid = await client.Inbox.ReplaceAsync (new UniqueId ((uint) i + 1), request);
					} else if (withInternalDates) {
						uid = await client.Inbox.ReplaceAsync (new UniqueId ((uint) i + 1), messages[i], flags[i], internalDates[i]);
					} else {
						uid = await client.Inbox.ReplaceAsync (new UniqueId ((uint) i + 1), messages[i], flags[i]);
					}

					Assert.That (uid.HasValue, Is.True, "Expected a UIDAPPEND resp-code");
					Assert.That (uid.Value.Id, Is.EqualTo (i + 1), "Unexpected UID");
				}

				await client.DisconnectAsync (true);

				foreach (var message in messages)
					message.Dispose ();
			}
		}

		static List<ImapReplayCommand> CreateCreateRenameDeleteCommands ()
		{
			return new List<ImapReplayCommand> {
				new ImapReplayCommand ("", "gmail.greeting.txt"),
				new ImapReplayCommand ("A00000000 CAPABILITY\r\n", "gmail.capability.txt"),
				new ImapReplayCommand ("A00000001 AUTHENTICATE PLAIN AHVzZXJuYW1lAHBhc3N3b3Jk\r\n", "gmail.authenticate.txt"),
				new ImapReplayCommand ("A00000002 NAMESPACE\r\n", "gmail.namespace.txt"),
				new ImapReplayCommand ("A00000003 LIST \"\" \"INBOX\" RETURN (SUBSCRIBED CHILDREN)\r\n", "gmail.list-inbox.txt"),
				new ImapReplayCommand ("A00000004 XLIST \"\" \"*\"\r\n", "gmail.xlist.txt"),
				new ImapReplayCommand ("A00000005 CREATE TopLevel1\r\n", ImapReplayCommandResponse.OK),
				new ImapReplayCommand ("A00000006 LIST \"\" TopLevel1\r\n", "gmail.list-toplevel1.txt"),
				new ImapReplayCommand ("A00000007 CREATE TopLevel2\r\n", ImapReplayCommandResponse.OK),
				new ImapReplayCommand ("A00000008 LIST \"\" TopLevel2\r\n", "gmail.list-toplevel2.txt"),
				new ImapReplayCommand ("A00000009 CREATE TopLevel1/SubLevel1\r\n", ImapReplayCommandResponse.OK),
				new ImapReplayCommand ("A00000010 LIST \"\" TopLevel1/SubLevel1\r\n", "gmail.list-sublevel1.txt"),
				new ImapReplayCommand ("A00000011 CREATE TopLevel2/SubLevel2\r\n", ImapReplayCommandResponse.OK),
				new ImapReplayCommand ("A00000012 LIST \"\" TopLevel2/SubLevel2\r\n", "gmail.list-sublevel2.txt"),
				new ImapReplayCommand ("A00000013 SELECT TopLevel1/SubLevel1 (CONDSTORE)\r\n", "gmail.select-sublevel1.txt"),
				new ImapReplayCommand ("A00000014 RENAME TopLevel1/SubLevel1 TopLevel2/SubLevel1\r\n", ImapReplayCommandResponse.OK),
				new ImapReplayCommand ("A00000015 DELETE TopLevel1\r\n", ImapReplayCommandResponse.OK),
				new ImapReplayCommand ("A00000016 SELECT TopLevel2/SubLevel2 (CONDSTORE)\r\n", "gmail.select-sublevel2.txt"),
				new ImapReplayCommand ("A00000017 RENAME TopLevel2 TopLevel\r\n", ImapReplayCommandResponse.OK),
				new ImapReplayCommand ("A00000018 SELECT TopLevel (CONDSTORE)\r\n", "gmail.select-toplevel.txt"),
				new ImapReplayCommand ("A00000019 DELETE TopLevel\r\n", ImapReplayCommandResponse.OK),
				new ImapReplayCommand ("A00000020 LOGOUT\r\n", "gmail.logout.txt")
			};
		}

		[Test]
		public void TestCreateRenameDelete ()
		{
			var commands = CreateCreateRenameDeleteCommands ();

			using (var client = new ImapClient () { TagPrefix = 'A' }) {
				try {
					client.Connect (new ImapReplayStream (commands, false), "localhost", 143, SecureSocketOptions.None);
				} catch (Exception ex) {
					Assert.Fail ($"Did not expect an exception in Connect: {ex}");
				}

				Assert.That (client.IsConnected, Is.True, "Client failed to connect.");

				try {
					client.Authenticate ("username", "password");
				} catch (Exception ex) {
					Assert.Fail ($"Did not expect an exception in Authenticate: {ex}");
				}

				int top1Renamed = 0, top2Renamed = 0, sub1Renamed = 0, sub2Renamed = 0;
				int top1Deleted = 0, top2Deleted = 0, sub1Deleted = 0, sub2Deleted = 0;
				int top1Closed = 0, top2Closed = 0, sub1Closed = 0, sub2Closed = 0;
				var personal = client.GetFolder (client.PersonalNamespaces[0]);
				var toplevel1 = personal.Create ("TopLevel1", false);
				var toplevel2 = personal.Create ("TopLevel2", false);
				var sublevel1 = toplevel1.Create ("SubLevel1", true);
				var sublevel2 = toplevel2.Create ("SubLevel2", true);

				toplevel1.Renamed += (o, e) => { top1Renamed++; };
				toplevel2.Renamed += (o, e) => { top2Renamed++; };
				sublevel1.Renamed += (o, e) => { sub1Renamed++; };
				sublevel2.Renamed += (o, e) => { sub2Renamed++; };

				toplevel1.Deleted += (o, e) => { top1Deleted++; };
				toplevel2.Deleted += (o, e) => { top2Deleted++; };
				sublevel1.Deleted += (o, e) => { sub1Deleted++; };
				sublevel2.Deleted += (o, e) => { sub2Deleted++; };

				toplevel1.Closed += (o, e) => { top1Closed++; };
				toplevel2.Closed += (o, e) => { top2Closed++; };
				sublevel1.Closed += (o, e) => { sub1Closed++; };
				sublevel2.Closed += (o, e) => { sub2Closed++; };

				Assert.That (sublevel1.CanOpen, Is.True, "SubLevel1 can be opened");
				sublevel1.Open (FolderAccess.ReadWrite);
				sublevel1.Rename (toplevel2, "SubLevel1");

				Assert.That (sub1Renamed, Is.EqualTo (1), "SubLevel1 folder should have received a Renamed event");
				Assert.That (sub1Closed, Is.EqualTo (1), "SubLevel1 should have received a Closed event");
				Assert.That (sublevel1.IsOpen, Is.False, "SubLevel1 should be closed after being renamed");

				toplevel1.Delete ();
				Assert.That (top1Deleted, Is.EqualTo (1), "TopLevel1 should have received a Deleted event");
				Assert.That (toplevel1.Exists, Is.False, "TopLevel1.Exists");

				Assert.That (sublevel2.CanOpen, Is.True, "SubLevel2 can be opened");
				sublevel2.Open (FolderAccess.ReadWrite);
				toplevel2.Rename (personal, "TopLevel");

				Assert.That (sub1Renamed, Is.EqualTo (2), "SubLevel1 folder should have received a Renamed event");
				Assert.That (sub2Renamed, Is.EqualTo (1), "SubLevel2 folder should have received a Renamed event");
				Assert.That (sub2Closed, Is.EqualTo (1), "SubLevel2 should have received a Closed event");
				Assert.That (sublevel2.IsOpen, Is.False, "SubLevel2 should be closed after being renamed");
				Assert.That (top2Renamed, Is.EqualTo (1), "TopLevel2 folder should have received a Renamed event");

				toplevel2.Open (FolderAccess.ReadWrite);
				toplevel2.Delete ();
				Assert.That (top2Closed, Is.EqualTo (1), "TopLevel2 should have received a Closed event");
				Assert.That (toplevel2.IsOpen, Is.False, "TopLevel2 should be closed after being deleted");
				Assert.That (top2Deleted, Is.EqualTo (1), "TopLevel2 should have received a Deleted event");
				Assert.That (toplevel2.Exists, Is.False, "TopLevel2.Exists");

				client.Disconnect (true);
			}
		}

		[Test]
		public async Task TestCreateRenameDeleteAsync ()
		{
			var commands = CreateCreateRenameDeleteCommands ();

			using (var client = new ImapClient () { TagPrefix = 'A' }) {
				try {
					await client.ConnectAsync (new ImapReplayStream (commands, true), "localhost", 143, SecureSocketOptions.None);
				} catch (Exception ex) {
					Assert.Fail ($"Did not expect an exception in Connect: {ex}");
				}

				Assert.That (client.IsConnected, Is.True, "Client failed to connect.");

				try {
					await client.AuthenticateAsync ("username", "password");
				} catch (Exception ex) {
					Assert.Fail ($"Did not expect an exception in Authenticate: {ex}");
				}

				int top1Renamed = 0, top2Renamed = 0, sub1Renamed = 0, sub2Renamed = 0;
				int top1Deleted = 0, top2Deleted = 0, sub1Deleted = 0, sub2Deleted = 0;
				int top1Closed = 0, top2Closed = 0, sub1Closed = 0, sub2Closed = 0;
				var personal = client.GetFolder (client.PersonalNamespaces[0]);
				var toplevel1 = await personal.CreateAsync ("TopLevel1", false);
				var toplevel2 = await personal.CreateAsync ("TopLevel2", false);
				var sublevel1 = await toplevel1.CreateAsync ("SubLevel1", true);
				var sublevel2 = await toplevel2.CreateAsync ("SubLevel2", true);

				toplevel1.Renamed += (o, e) => { top1Renamed++; };
				toplevel2.Renamed += (o, e) => { top2Renamed++; };
				sublevel1.Renamed += (o, e) => { sub1Renamed++; };
				sublevel2.Renamed += (o, e) => { sub2Renamed++; };

				toplevel1.Deleted += (o, e) => { top1Deleted++; };
				toplevel2.Deleted += (o, e) => { top2Deleted++; };
				sublevel1.Deleted += (o, e) => { sub1Deleted++; };
				sublevel2.Deleted += (o, e) => { sub2Deleted++; };

				toplevel1.Closed += (o, e) => { top1Closed++; };
				toplevel2.Closed += (o, e) => { top2Closed++; };
				sublevel1.Closed += (o, e) => { sub1Closed++; };
				sublevel2.Closed += (o, e) => { sub2Closed++; };

				Assert.That (sublevel1.CanOpen, Is.True, "SubLevel1 can be opened");
				await sublevel1.OpenAsync (FolderAccess.ReadWrite);
				await sublevel1.RenameAsync (toplevel2, "SubLevel1");

				Assert.That (sub1Renamed, Is.EqualTo (1), "SubLevel1 folder should have received a Renamed event");
				Assert.That (sub1Closed, Is.EqualTo (1), "SubLevel1 should have received a Closed event");
				Assert.That (sublevel1.IsOpen, Is.False, "SubLevel1 should be closed after being renamed");

				await toplevel1.DeleteAsync ();
				Assert.That (top1Deleted, Is.EqualTo (1), "TopLevel1 should have received a Deleted event");
				Assert.That (toplevel1.Exists, Is.False, "TopLevel1.Exists");

				Assert.That (sublevel2.CanOpen, Is.True, "SubLevel2 can be opened");
				await sublevel2.OpenAsync (FolderAccess.ReadWrite);
				await toplevel2.RenameAsync (personal, "TopLevel");

				Assert.That (sub1Renamed, Is.EqualTo (2), "SubLevel1 folder should have received a Renamed event");
				Assert.That (sub2Renamed, Is.EqualTo (1), "SubLevel2 folder should have received a Renamed event");
				Assert.That (sub2Closed, Is.EqualTo (1), "SubLevel2 should have received a Closed event");
				Assert.That (sublevel2.IsOpen, Is.False, "SubLevel2 should be closed after being renamed");
				Assert.That (top2Renamed, Is.EqualTo (1), "TopLevel2 folder should have received a Renamed event");

				await toplevel2.OpenAsync (FolderAccess.ReadWrite);
				await toplevel2.DeleteAsync ();
				Assert.That (top2Closed, Is.EqualTo (1), "TopLevel2 should have received a Closed event");
				Assert.That (toplevel2.IsOpen, Is.False, "TopLevel2 should be closed after being deleted");
				Assert.That (top2Deleted, Is.EqualTo (1), "TopLevel2 should have received a Deleted event");
				Assert.That (toplevel2.Exists, Is.False, "TopLevel2.Exists");

				await client.DisconnectAsync (true);
			}
		}

		static List<ImapReplayCommand> CreateCreateMailboxIdCommands ()
		{
			return new List<ImapReplayCommand> {
				new ImapReplayCommand ("", "gmail.greeting.txt"),
				new ImapReplayCommand ("A00000000 CAPABILITY\r\n", "gmail.capability.txt"),
				new ImapReplayCommand ("A00000001 AUTHENTICATE PLAIN AHVzZXJuYW1lAHBhc3N3b3Jk\r\n", "gmail.authenticate+create-special-use.txt"),
				new ImapReplayCommand ("A00000002 NAMESPACE\r\n", "gmail.namespace.txt"),
				new ImapReplayCommand ("A00000003 LIST \"\" \"INBOX\" RETURN (SUBSCRIBED CHILDREN)\r\n", "gmail.list-inbox.txt"),
				new ImapReplayCommand ("A00000004 XLIST \"\" \"*\"\r\n", "gmail.xlist.txt"),
				new ImapReplayCommand ("A00000005 CREATE TopLevel1\r\n", "gmail.create-mailboxid.txt"),
				new ImapReplayCommand ("A00000006 LIST \"\" TopLevel1\r\n", "gmail.list-toplevel1.txt"),
				new ImapReplayCommand ("A00000007 LOGOUT\r\n", "gmail.logout.txt")
			};
		}

		[Test]
		public void TestCreateMailboxId ()
		{
			var commands = CreateCreateMailboxIdCommands ();

			using (var client = new ImapClient () { TagPrefix = 'A' }) {
				try {
					client.Connect (new ImapReplayStream (commands, false), "localhost", 143, SecureSocketOptions.None);
				} catch (Exception ex) {
					Assert.Fail ($"Did not expect an exception in Connect: {ex}");
				}

				Assert.That (client.IsConnected, Is.True, "Client failed to connect.");

				try {
					client.Authenticate ("username", "password");
				} catch (Exception ex) {
					Assert.Fail ($"Did not expect an exception in Authenticate: {ex}");
				}

				Assert.That (client.Capabilities.HasFlag (ImapCapabilities.ObjectID), Is.True, "OBJECTID");

				var personal = client.GetFolder (client.PersonalNamespaces[0]);
				var toplevel1 = personal.Create ("TopLevel1", true);
				Assert.That (toplevel1.Attributes, Is.EqualTo (FolderAttributes.HasNoChildren));
				Assert.That (toplevel1.Id, Is.EqualTo ("25dcfa84-fd65-41c3-abc3-633c8f10923f"));

				client.Disconnect (true);
			}
		}

		[Test]
		public async Task TestCreateMailboxIdAsync ()
		{
			var commands = CreateCreateMailboxIdCommands ();

			using (var client = new ImapClient () { TagPrefix = 'A' }) {
				try {
					await client.ConnectAsync (new ImapReplayStream (commands, true), "localhost", 143, SecureSocketOptions.None);
				} catch (Exception ex) {
					Assert.Fail ($"Did not expect an exception in Connect: {ex}");
				}

				Assert.That (client.IsConnected, Is.True, "Client failed to connect.");

				try {
					await client.AuthenticateAsync ("username", "password");
				} catch (Exception ex) {
					Assert.Fail ($"Did not expect an exception in Authenticate: {ex}");
				}

				Assert.That (client.Capabilities.HasFlag (ImapCapabilities.ObjectID), Is.True, "OBJECTID");

				var personal = client.GetFolder (client.PersonalNamespaces[0]);
				var toplevel1 = await personal.CreateAsync ("TopLevel1", true);
				Assert.That (toplevel1.Attributes, Is.EqualTo (FolderAttributes.HasNoChildren));
				Assert.That (toplevel1.Id, Is.EqualTo ("25dcfa84-fd65-41c3-abc3-633c8f10923f"));

				await client.DisconnectAsync (true);
			}
		}

		static List<ImapReplayCommand> CreateCreateSpecialUseCommands ()
		{
			return new List<ImapReplayCommand> {
				new ImapReplayCommand ("", "gmail.greeting.txt"),
				new ImapReplayCommand ("A00000000 CAPABILITY\r\n", "gmail.capability.txt"),
				new ImapReplayCommand ("A00000001 AUTHENTICATE PLAIN AHVzZXJuYW1lAHBhc3N3b3Jk\r\n", "gmail.authenticate+create-special-use.txt"),
				new ImapReplayCommand ("A00000002 NAMESPACE\r\n", "gmail.namespace.txt"),
				new ImapReplayCommand ("A00000003 LIST \"\" \"INBOX\" RETURN (SUBSCRIBED CHILDREN)\r\n", "gmail.list-inbox.txt"),
				new ImapReplayCommand ("A00000004 XLIST \"\" \"*\"\r\n", "gmail.xlist.txt"),
				new ImapReplayCommand ("A00000005 CREATE \"[Gmail]/Archives\" (USE (\\Archive))\r\n", "gmail.create-mailboxid.txt"),
				new ImapReplayCommand ("A00000006 LIST \"\" \"[Gmail]/Archives\"\r\n", "gmail.list-archives.txt"),
				new ImapReplayCommand ("A00000007 CREATE \"[Gmail]/Flagged\" (USE (\\Flagged))\r\n", "gmail.create-mailboxid.txt"),
				new ImapReplayCommand ("A00000008 LIST \"\" \"[Gmail]/Flagged\"\r\n", "gmail.list-flagged.txt"),
				new ImapReplayCommand ("A00000009 LOGOUT\r\n", "gmail.logout.txt")
			};
		}

		[Test]
		public void TestCreateSpecialUse ()
		{
			var commands = CreateCreateSpecialUseCommands ();

			using (var client = new ImapClient () { TagPrefix = 'A' }) {
				try {
					client.Connect (new ImapReplayStream (commands, false), "localhost", 143, SecureSocketOptions.None);
				} catch (Exception ex) {
					Assert.Fail ($"Did not expect an exception in Connect: {ex}");
				}

				Assert.That (client.IsConnected, Is.True, "Client failed to connect.");

				try {
					client.Authenticate ("username", "password");
				} catch (Exception ex) {
					Assert.Fail ($"Did not expect an exception in Authenticate: {ex}");
				}

				Assert.That (client.Capabilities.HasFlag (ImapCapabilities.CreateSpecialUse), Is.True, "CREATE-SPECIAL-USE");

				var personal = client.GetFolder (client.PersonalNamespaces[0]);
				var gmail = personal.GetSubfolder ("[Gmail]");

				var archives = gmail.Create ("Archives", SpecialFolder.Archive);
				Assert.That (archives.Attributes, Is.EqualTo (FolderAttributes.HasNoChildren | FolderAttributes.Archive));
				Assert.That (client.GetFolder (SpecialFolder.Archive), Is.EqualTo (archives));
				Assert.That (archives.Id, Is.EqualTo ("25dcfa84-fd65-41c3-abc3-633c8f10923f"));

				var flagged = gmail.Create ("Flagged", SpecialFolder.Flagged);
				Assert.That (flagged.Attributes, Is.EqualTo (FolderAttributes.HasNoChildren | FolderAttributes.Flagged));
				Assert.That (client.GetFolder (SpecialFolder.Flagged), Is.EqualTo (flagged));
				Assert.That (flagged.Id, Is.EqualTo ("25dcfa84-fd65-41c3-abc3-633c8f10923f"));

				client.Disconnect (true);
			}
		}

		[Test]
		public async Task TestCreateSpecialUseAsync ()
		{
			var commands = CreateCreateSpecialUseCommands ();

			using (var client = new ImapClient () { TagPrefix = 'A' }) {
				try {
					await client.ConnectAsync (new ImapReplayStream (commands, true), "localhost", 143, SecureSocketOptions.None);
				} catch (Exception ex) {
					Assert.Fail ($"Did not expect an exception in Connect: {ex}");
				}

				Assert.That (client.IsConnected, Is.True, "Client failed to connect.");

				try {
					await client.AuthenticateAsync ("username", "password");
				} catch (Exception ex) {
					Assert.Fail ($"Did not expect an exception in Authenticate: {ex}");
				}

				Assert.That (client.Capabilities.HasFlag (ImapCapabilities.CreateSpecialUse), Is.True, "CREATE-SPECIAL-USE");

				var personal = client.GetFolder (client.PersonalNamespaces[0]);
				var gmail = await personal.GetSubfolderAsync ("[Gmail]");

				var archives = await gmail.CreateAsync ("Archives", SpecialFolder.Archive);
				Assert.That (archives.Attributes, Is.EqualTo (FolderAttributes.HasNoChildren | FolderAttributes.Archive));
				Assert.That (client.GetFolder (SpecialFolder.Archive), Is.EqualTo (archives));
				Assert.That (archives.Id, Is.EqualTo ("25dcfa84-fd65-41c3-abc3-633c8f10923f"));

				var flagged = await gmail.CreateAsync ("Flagged", SpecialFolder.Flagged);
				Assert.That (flagged.Attributes, Is.EqualTo (FolderAttributes.HasNoChildren | FolderAttributes.Flagged));
				Assert.That (client.GetFolder (SpecialFolder.Flagged), Is.EqualTo (flagged));
				Assert.That (flagged.Id, Is.EqualTo ("25dcfa84-fd65-41c3-abc3-633c8f10923f"));

				await client.DisconnectAsync (true);
			}
		}

		static List<ImapReplayCommand> CreateCreateSpecialUseMultipleCommands ()
		{
			return new List<ImapReplayCommand> {
				new ImapReplayCommand ("", "gmail.greeting.txt"),
				new ImapReplayCommand ("A00000000 CAPABILITY\r\n", "gmail.capability.txt"),
				new ImapReplayCommand ("A00000001 AUTHENTICATE PLAIN AHVzZXJuYW1lAHBhc3N3b3Jk\r\n", "gmail.authenticate+create-special-use.txt"),
				new ImapReplayCommand ("A00000002 NAMESPACE\r\n", "gmail.namespace.txt"),
				new ImapReplayCommand ("A00000003 LIST \"\" \"INBOX\" RETURN (SUBSCRIBED CHILDREN)\r\n", "gmail.list-inbox.txt"),
				new ImapReplayCommand ("A00000004 XLIST \"\" \"*\"\r\n", "gmail.xlist.txt"),
				new ImapReplayCommand ("A00000005 CREATE \"[Gmail]/Archives\" (USE (\\All \\Archive \\Drafts \\Flagged \\Important \\Junk \\Sent \\Trash))\r\n", "gmail.create-mailboxid.txt"),
				new ImapReplayCommand ("A00000006 LIST \"\" \"[Gmail]/Archives\"\r\n", "gmail.list-archives.txt"),
				new ImapReplayCommand ("A00000007 CREATE \"[Gmail]/MyImportant\" (USE (\\Important))\r\n", Encoding.ASCII.GetBytes ("A00000007 NO [USEATTR] An \\Important mailbox already exists\r\n")),
				new ImapReplayCommand ("A00000008 LOGOUT\r\n", "gmail.logout.txt")
			};
		}

		[Test]
		public void TestCreateSpecialUseMultiple ()
		{
			var commands = CreateCreateSpecialUseMultipleCommands ();

			using (var client = new ImapClient () { TagPrefix = 'A' }) {
				try {
					client.Connect (new ImapReplayStream (commands, false), "localhost", 143, SecureSocketOptions.None);
				} catch (Exception ex) {
					Assert.Fail ($"Did not expect an exception in Connect: {ex}");
				}

				Assert.That (client.IsConnected, Is.True, "Client failed to connect.");

				try {
					client.Authenticate ("username", "password");
				} catch (Exception ex) {
					Assert.Fail ($"Did not expect an exception in Authenticate: {ex}");
				}

				Assert.That (client.Capabilities.HasFlag (ImapCapabilities.CreateSpecialUse), Is.True, "CREATE-SPECIAL-USE");

				var personal = client.GetFolder (client.PersonalNamespaces[0]);
				var gmail = personal.GetSubfolder ("[Gmail]");

				var uses = new List<SpecialFolder> {
					SpecialFolder.All,
					SpecialFolder.Archive,
					SpecialFolder.Drafts,
					SpecialFolder.Flagged,
					SpecialFolder.Important,
					SpecialFolder.Junk,
					SpecialFolder.Sent,
					SpecialFolder.Trash,

					// specifically duplicate some special uses
					SpecialFolder.All,
					SpecialFolder.Flagged,

					// and add one that is invalid
					(SpecialFolder) 15
				};

				var archive = gmail.Create ("Archives", uses);
				Assert.That (archive.Attributes, Is.EqualTo (FolderAttributes.HasNoChildren | FolderAttributes.Archive));
				Assert.That (client.GetFolder (SpecialFolder.Archive), Is.EqualTo (archive));
				Assert.That (archive.Id, Is.EqualTo ("25dcfa84-fd65-41c3-abc3-633c8f10923f"));

				try {
					gmail.Create ("MyImportant", new[] { SpecialFolder.Important });
					Assert.Fail ("Creating the MyImportant folder should have thrown an ImapCommandException");
				} catch (ImapCommandException ex) {
					Assert.That (ex.Response, Is.EqualTo (ImapCommandResponse.No));
					Assert.That (ex.ResponseText, Is.EqualTo ("An \\Important mailbox already exists"));
				} catch (Exception ex) {
					Assert.Fail ($"Unexpected exception: {ex}");
				}

				client.Disconnect (true);
			}
		}

		[Test]
		public async Task TestCreateSpecialUseMultipleAsync ()
		{
			var commands = CreateCreateSpecialUseMultipleCommands ();

			using (var client = new ImapClient () { TagPrefix = 'A' }) {
				try {
					await client.ConnectAsync (new ImapReplayStream (commands, true), "localhost", 143, SecureSocketOptions.None);
				} catch (Exception ex) {
					Assert.Fail ($"Did not expect an exception in Connect: {ex}");
				}

				Assert.That (client.IsConnected, Is.True, "Client failed to connect.");

				try {
					await client.AuthenticateAsync ("username", "password");
				} catch (Exception ex) {
					Assert.Fail ($"Did not expect an exception in Authenticate: {ex}");
				}

				Assert.That (client.Capabilities.HasFlag (ImapCapabilities.CreateSpecialUse), Is.True, "CREATE-SPECIAL-USE");

				var personal = client.GetFolder (client.PersonalNamespaces[0]);
				var gmail = await personal.GetSubfolderAsync ("[Gmail]");

				var uses = new List<SpecialFolder> {
					SpecialFolder.All,
					SpecialFolder.Archive,
					SpecialFolder.Drafts,
					SpecialFolder.Flagged,
					SpecialFolder.Important,
					SpecialFolder.Junk,
					SpecialFolder.Sent,
					SpecialFolder.Trash,

					// specifically duplicate some special uses
					SpecialFolder.All,
					SpecialFolder.Flagged,

					// and add one that is invalid
					(SpecialFolder) 15
				};

				var archive = await gmail.CreateAsync ("Archives", uses);
				Assert.That (archive.Attributes, Is.EqualTo (FolderAttributes.HasNoChildren | FolderAttributes.Archive));
				Assert.That (client.GetFolder (SpecialFolder.Archive), Is.EqualTo (archive));
				Assert.That (archive.Id, Is.EqualTo ("25dcfa84-fd65-41c3-abc3-633c8f10923f"));

				try {
					await gmail.CreateAsync ("MyImportant", new[] { SpecialFolder.Important });
					Assert.Fail ("Creating the MyImportamnt folder should have thrown an ImapCommandException");
				} catch (ImapCommandException ex) {
					Assert.That (ex.Response, Is.EqualTo (ImapCommandResponse.No));
					Assert.That (ex.ResponseText, Is.EqualTo ("An \\Important mailbox already exists"));
				} catch (Exception ex) {
					Assert.Fail ($"Unexpected exception: {ex}");
				}

				await client.DisconnectAsync (true);
			}
		}

		static List<ImapReplayCommand> CreateCopyToCommands ()
		{
			return new List<ImapReplayCommand> {
				new ImapReplayCommand ("", "gmail.greeting.txt"),
				new ImapReplayCommand ("A00000000 CAPABILITY\r\n", "gmail.capability.txt"),
				new ImapReplayCommand ("A00000001 AUTHENTICATE PLAIN AHVzZXJuYW1lAHBhc3N3b3Jk\r\n", "gmail.authenticate.txt"),
				new ImapReplayCommand ("A00000002 NAMESPACE\r\n", "gmail.namespace.txt"),
				new ImapReplayCommand ("A00000003 LIST \"\" \"INBOX\" RETURN (SUBSCRIBED CHILDREN)\r\n", "gmail.list-inbox.txt"),
				new ImapReplayCommand ("A00000004 XLIST \"\" \"*\"\r\n", "gmail.xlist.txt"),
				new ImapReplayCommand ("A00000005 SELECT INBOX (CONDSTORE)\r\n", "gmail.select-inbox.txt"),
				new ImapReplayCommand ("A00000006 UID SEARCH RETURN (ALL) ALL\r\n", "gmail.search.txt"),
				new ImapReplayCommand ("A00000007 LIST \"\" \"Archived Messages\"\r\n", "gmail.list-archived-messages.txt"),
				new ImapReplayCommand ("A00000008 UID COPY 1:3,5,7:9,11:14,26:29,31,34,41:43,50 \"Archived Messages\"\r\n", "gmail.uid-copy.txt"),
				new ImapReplayCommand ("A00000009 SEARCH UID 1:3,5,7:9,11:14,26:29,31,34,41:43,50\r\n", "gmail.get-indexes.txt"),
				new ImapReplayCommand ("A00000010 COPY 1:21 \"Archived Messages\"\r\n", "gmail.uid-copy.txt"),
				new ImapReplayCommand ("A00000011 LOGOUT\r\n", "gmail.logout.txt")
			};
		}

		[Test]
		public void TestCopyTo ()
		{
			var commands = CreateCopyToCommands ();

			using (var client = new ImapClient () { TagPrefix = 'A' }) {
				try {
					client.Connect (new ImapReplayStream (commands, false), "localhost", 143, SecureSocketOptions.None);
				} catch (Exception ex) {
					Assert.Fail ($"Did not expect an exception in Connect: {ex}");
				}

				Assert.That (client.IsConnected, Is.True, "Client failed to connect.");

				try {
					client.Authenticate ("username", "password");
				} catch (Exception ex) {
					Assert.Fail ($"Did not expect an exception in Authenticate: {ex}");
				}

				Assert.That (client.Capabilities.HasFlag (ImapCapabilities.UidPlus), Is.True, "Expected UIDPLUS extension");

				var personal = client.GetFolder (client.PersonalNamespaces[0]);
				var inbox = client.Inbox;

				inbox.Open (FolderAccess.ReadWrite);
				var uids = inbox.Search (SearchQuery.All);

				var archived = personal.GetSubfolder ("Archived Messages");

				// Test copying using the UIDPLUS extension
				var copied = inbox.CopyTo (uids, archived);

				Assert.That (copied.Destination, Has.Count.EqualTo (copied.Source.Count), "Source and Destination UID counts do not match");

				// Disable UIDPLUS and try again (to test GetIndexesAsync() and CopyTo(IList<int>, IMailFolder)
				client.Capabilities &= ~ImapCapabilities.UidPlus;
				copied = inbox.CopyTo (uids, archived);

				Assert.That (copied.Destination, Has.Count.EqualTo (copied.Source.Count), "Source and Destination UID counts do not match");

				client.Disconnect (true);
			}
		}

		[Test]
		public async Task TestCopyToAsync ()
		{
			var commands = CreateCopyToCommands ();

			using (var client = new ImapClient () { TagPrefix = 'A' }) {
				try {
					await client.ConnectAsync (new ImapReplayStream (commands, true), "localhost", 143, SecureSocketOptions.None);
				} catch (Exception ex) {
					Assert.Fail ($"Did not expect an exception in Connect: {ex}");
				}

				Assert.That (client.IsConnected, Is.True, "Client failed to connect.");

				try {
					await client.AuthenticateAsync ("username", "password");
				} catch (Exception ex) {
					Assert.Fail ($"Did not expect an exception in Authenticate: {ex}");
				}

				Assert.That (client.Capabilities.HasFlag (ImapCapabilities.UidPlus), Is.True, "Expected UIDPLUS extension");

				var personal = client.GetFolder (client.PersonalNamespaces[0]);
				var inbox = client.Inbox;

				await inbox.OpenAsync (FolderAccess.ReadWrite);
				var uids = await inbox.SearchAsync (SearchQuery.All);

				var archived = await personal.GetSubfolderAsync ("Archived Messages");

				// Test copying using the UIDPLUS extension
				var copied = await inbox.CopyToAsync (uids, archived);

				Assert.That (copied.Destination, Has.Count.EqualTo (copied.Source.Count), "Source and Destination UID counts do not match");

				// Disable UIDPLUS and try again (to test GetIndexesAsync() and CopyTo(IList<int>, IMailFolder)
				client.Capabilities &= ~ImapCapabilities.UidPlus;
				copied = await inbox.CopyToAsync (uids, archived);

				Assert.That (copied.Destination, Has.Count.EqualTo (copied.Source.Count), "Source and Destination UID counts do not match");

				await client.DisconnectAsync (true);
			}
		}

		static List<ImapReplayCommand> CreateExchangeCopyUidRespCodeWithoutOkCommands ()
		{
			return new List<ImapReplayCommand> {
				new ImapReplayCommand ("", "exchange.greeting.txt"),
				new ImapReplayCommand ("A00000000 CAPABILITY\r\n", "exchange.capability-preauth.txt"),
				new ImapReplayCommand ("A00000001 AUTHENTICATE PLAIN AHVzZXJuYW1lAHBhc3N3b3Jk\r\n", ImapReplayCommandResponse.OK),
				new ImapReplayCommand ("A00000002 CAPABILITY\r\n", "exchange.capability-postauth.txt"),
				new ImapReplayCommand ("A00000003 NAMESPACE\r\n", "gmail.namespace.txt"),
				new ImapReplayCommand ("A00000004 LIST \"\" \"INBOX\"\r\n", "common.list-inbox.txt"),
				new ImapReplayCommand ("A00000005 SELECT INBOX\r\n", "common.select-inbox.txt"),
				new ImapReplayCommand ("A00000006 LIST \"\" Level1\r\n", "gmail.list-level1.txt"),
				new ImapReplayCommand ("A00000007 UID MOVE 31 Level1\r\n", "exchange.issue115.txt"),
				new ImapReplayCommand ("A00000008 LOGOUT\r\n", "gmail.logout.txt")
			};
		}

		[Test]
		public void TestExchangeCopyUidRespCodeWithoutOk ()
		{
			var commands = CreateExchangeCopyUidRespCodeWithoutOkCommands ();

			using (var client = new ImapClient () { TagPrefix = 'A' }) {
				try {
					client.Connect (new ImapReplayStream (commands, false), "localhost", 143, SecureSocketOptions.None);
				} catch (Exception ex) {
					Assert.Fail ($"Did not expect an exception in Connect: {ex}");
				}

				Assert.That (client.IsConnected, Is.True, "Client failed to connect.");

				try {
					client.Authenticate ("username", "password");
				} catch (Exception ex) {
					Assert.Fail ($"Did not expect an exception in Authenticate: {ex}");
				}

				Assert.That (client.Capabilities.HasFlag (ImapCapabilities.UidPlus), Is.True, "Expected UIDPLUS extension");

				var personal = client.GetFolder (client.PersonalNamespaces[0]);
				var inbox = client.Inbox;

				inbox.Open (FolderAccess.ReadWrite);

				// Test handling of broken Exchange IMAP response: "[COPYUID 55 31 6]" (it should be "* OK [COPYUID 55 31 6]")
				var level1 = personal.GetSubfolder ("Level1");
				var uids = new[] { new UniqueId (31) };
				var copied = inbox.MoveTo (uids, level1);

				Assert.That (copied.Destination, Has.Count.EqualTo (copied.Source.Count), "Source and Destination UID counts do not match");
				Assert.That (uids[0], Is.EqualTo (copied.Source[0]), "Source[0]");
				Assert.That (new UniqueId (6), Is.EqualTo (copied.Destination[0]), "Destination[0]");

				client.Disconnect (true);
			}
		}

		[Test]
		public async Task TestExchangeCopyUidRespCodeWithoutOkAsync ()
		{
			var commands = CreateExchangeCopyUidRespCodeWithoutOkCommands ();

			using (var client = new ImapClient () { TagPrefix = 'A' }) {
				try {
					await client.ConnectAsync (new ImapReplayStream (commands, true), "localhost", 143, SecureSocketOptions.None);
				} catch (Exception ex) {
					Assert.Fail ($"Did not expect an exception in Connect: {ex}");
				}

				Assert.That (client.IsConnected, Is.True, "Client failed to connect.");

				try {
					await client.AuthenticateAsync ("username", "password");
				} catch (Exception ex) {
					Assert.Fail ($"Did not expect an exception in Authenticate: {ex}");
				}

				Assert.That (client.Capabilities.HasFlag (ImapCapabilities.UidPlus), Is.True, "Expected UIDPLUS extension");

				var personal = client.GetFolder (client.PersonalNamespaces[0]);
				var inbox = client.Inbox;

				await inbox.OpenAsync (FolderAccess.ReadWrite);

				// Test handling of broken Exchange IMAP response: "[COPYUID 55 31 6]" (it should be "* OK [COPYUID 55 31 6]")
				var level1 = await personal.GetSubfolderAsync ("Level1");
				var uids = new[] { new UniqueId (31) };
				var copied = await inbox.MoveToAsync (uids, level1);

				Assert.That (copied.Destination, Has.Count.EqualTo (copied.Source.Count), "Source and Destination UID counts do not match");
				Assert.That (uids[0], Is.EqualTo (copied.Source[0]), "Source[0]");
				Assert.That (new UniqueId (6), Is.EqualTo (copied.Destination[0]), "Destination[0]");

				await client.DisconnectAsync (true);
			}
		}

		static List<ImapReplayCommand> CreateMoveToCommands ()
		{
			var commands = new List<ImapReplayCommand> {
				new ImapReplayCommand ("", "gmail.greeting.txt"),
				new ImapReplayCommand ("A00000000 CAPABILITY\r\n", "gmail.capability.txt"),
				new ImapReplayCommand ("A00000001 AUTHENTICATE PLAIN AHVzZXJuYW1lAHBhc3N3b3Jk\r\n", "gmail.authenticate.txt"),
				new ImapReplayCommand ("A00000002 NAMESPACE\r\n", "gmail.namespace.txt"),
				new ImapReplayCommand ("A00000003 LIST \"\" \"INBOX\" RETURN (SUBSCRIBED CHILDREN)\r\n", "gmail.list-inbox.txt"),
				new ImapReplayCommand ("A00000004 XLIST \"\" \"*\"\r\n", "gmail.xlist.txt"),
				new ImapReplayCommand ("A00000005 SELECT INBOX (CONDSTORE)\r\n", "gmail.select-inbox.txt"),
				new ImapReplayCommand ("A00000006 LIST \"\" \"Archived Messages\"\r\n", "gmail.list-archived-messages.txt"),
				new ImapReplayCommand ("A00000007 MOVE 1:21 \"Archived Messages\"\r\n", ImapReplayCommandResponse.OK),
				new ImapReplayCommand ("A00000008 COPY 1:21 \"Archived Messages\"\r\n", ImapReplayCommandResponse.OK),
				new ImapReplayCommand ("A00000009 STORE 1:21 +FLAGS.SILENT (\\Deleted)\r\n", ImapReplayCommandResponse.OK),
				new ImapReplayCommand ("A00000010 LOGOUT\r\n", "gmail.logout.txt")
			};

			return commands;
		}

		[Test]
		public void TestMoveTo ()
		{
			var commands = CreateMoveToCommands ();

			using (var client = new ImapClient () { TagPrefix = 'A' }) {
				try {
					client.Connect (new ImapReplayStream (commands, false), "localhost", 143, SecureSocketOptions.None);
				} catch (Exception ex) {
					Assert.Fail ($"Did not expect an exception in Connect: {ex}");
				}

				Assert.That (client.IsConnected, Is.True, "Client failed to connect.");

				try {
					client.Authenticate ("username", "password");
				} catch (Exception ex) {
					Assert.Fail ($"Did not expect an exception in Authenticate: {ex}");
				}

				Assert.That (client.Capabilities.HasFlag (ImapCapabilities.UidPlus), Is.True, "Expected UIDPLUS extension");

				var personal = client.GetFolder (client.PersonalNamespaces[0]);
				var inbox = client.Inbox;

				inbox.Open (FolderAccess.ReadWrite);

				var indexes = new int[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20 };
				var archived = personal.GetSubfolder ("Archived Messages");

				inbox.MoveTo (indexes, archived);

				client.Capabilities &= ~ImapCapabilities.Move;
				inbox.MoveTo (indexes, archived);

				client.Disconnect (true);
			}
		}

		[Test]
		public async Task TestMoveToAsync ()
		{
			var commands = CreateMoveToCommands ();

			using (var client = new ImapClient () { TagPrefix = 'A' }) {
				try {
					await client.ConnectAsync (new ImapReplayStream (commands, true), "localhost", 143, SecureSocketOptions.None);
				} catch (Exception ex) {
					Assert.Fail ($"Did not expect an exception in Connect: {ex}");
				}

				Assert.That (client.IsConnected, Is.True, "Client failed to connect.");

				try {
					await client.AuthenticateAsync ("username", "password");
				} catch (Exception ex) {
					Assert.Fail ($"Did not expect an exception in Authenticate: {ex}");
				}

				Assert.That (client.Capabilities.HasFlag (ImapCapabilities.UidPlus), Is.True, "Expected UIDPLUS extension");

				var personal = client.GetFolder (client.PersonalNamespaces[0]);
				var inbox = client.Inbox;

				await inbox.OpenAsync (FolderAccess.ReadWrite);

				var indexes = new int[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20 };
				var archived = await personal.GetSubfolderAsync ("Archived Messages");

				await inbox.MoveToAsync (indexes, archived);

				client.Capabilities &= ~ImapCapabilities.Move;
				await inbox.MoveToAsync (indexes, archived);

				await client.DisconnectAsync (true);
			}
		}

		static List<ImapReplayCommand> CreateUidMoveToCommands (bool disableMove)
		{
			var commands = new List<ImapReplayCommand> {
				new ImapReplayCommand ("", "gmail.greeting.txt"),
				new ImapReplayCommand ("A00000000 CAPABILITY\r\n", "gmail.capability.txt"),
				new ImapReplayCommand ("A00000001 AUTHENTICATE PLAIN AHVzZXJuYW1lAHBhc3N3b3Jk\r\n", "gmail.authenticate.txt"),
				new ImapReplayCommand ("A00000002 NAMESPACE\r\n", "gmail.namespace.txt"),
				new ImapReplayCommand ("A00000003 LIST \"\" \"INBOX\" RETURN (SUBSCRIBED CHILDREN)\r\n", "gmail.list-inbox.txt"),
				new ImapReplayCommand ("A00000004 XLIST \"\" \"*\"\r\n", "gmail.xlist.txt"),
				new ImapReplayCommand ("A00000005 SELECT INBOX (CONDSTORE)\r\n", "gmail.select-inbox.txt"),
				new ImapReplayCommand ("A00000006 UID SEARCH RETURN (ALL) ALL\r\n", "gmail.search.txt"),
				new ImapReplayCommand ("A00000007 LIST \"\" \"Archived Messages\"\r\n", "gmail.list-archived-messages.txt"),
				new ImapReplayCommand ("A00000008 UID MOVE 1:3,5,7:9,11:14,26:29,31,34,41:43,50 \"Archived Messages\"\r\n", "gmail.uid-move.txt")
			};
			if (disableMove) {
				commands.Add (new ImapReplayCommand ("A00000009 UID COPY 1:3,5,7:9,11:14,26:29,31,34,41:43,50 \"Archived Messages\"\r\n", "gmail.uid-copy.txt"));
				commands.Add (new ImapReplayCommand ("A00000010 UID STORE 1:3,5,7:9,11:14,26:29,31,34,41:43,50 +FLAGS.SILENT (\\Deleted)\r\n", ImapReplayCommandResponse.OK));
				commands.Add (new ImapReplayCommand ("A00000011 UID EXPUNGE 1:3,5,7:9,11:14,26:29,31,34,41:43,50\r\n", "gmail.uid-expunge.txt"));
				commands.Add (new ImapReplayCommand ("A00000012 LOGOUT\r\n", "gmail.logout.txt"));
			} else {
				commands.Add (new ImapReplayCommand ("A00000009 SEARCH UID 1:3,5,7:9,11:14,26:29,31,34,41:43,50\r\n", "gmail.get-indexes.txt"));
				commands.Add (new ImapReplayCommand ("A00000010 MOVE 1:21 \"Archived Messages\"\r\n", "gmail.uid-move.txt"));
				commands.Add (new ImapReplayCommand ("A00000011 LOGOUT\r\n", "gmail.logout.txt"));
			}

			return commands;
		}

		[TestCase (true, TestName = "TestUidMoveToDisableMove")]
		[TestCase (false, TestName = "TestUidMoveToDisableUidPlus")]
		public void TestUidMoveTo (bool disableMove)
		{
			var commands = CreateUidMoveToCommands (disableMove);

			using (var client = new ImapClient () { TagPrefix = 'A' }) {
				try {
					client.Connect (new ImapReplayStream (commands, false), "localhost", 143, SecureSocketOptions.None);
				} catch (Exception ex) {
					Assert.Fail ($"Did not expect an exception in Connect: {ex}");
				}

				Assert.That (client.IsConnected, Is.True, "Client failed to connect.");

				try {
					client.Authenticate ("username", "password");
				} catch (Exception ex) {
					Assert.Fail ($"Did not expect an exception in Authenticate: {ex}");
				}

				Assert.That (client.Capabilities.HasFlag (ImapCapabilities.UidPlus), Is.True, "Expected UIDPLUS extension");

				var personal = client.GetFolder (client.PersonalNamespaces [0]);
				var inbox = client.Inbox;

				inbox.Open (FolderAccess.ReadWrite);
				var uids = inbox.Search (SearchQuery.All);

				var archived = personal.GetSubfolder ("Archived Messages");
				int changed = 0, expunged = 0;

				inbox.MessageExpunged += (o, e) => { expunged++; Assert.That (e.Index, Is.EqualTo (0), "Expunged event message index"); };
				inbox.CountChanged += (o, e) => { changed++; };

				// Test copying using the MOVE & UIDPLUS extensions
				var moved = inbox.MoveTo (uids, archived);

				Assert.That (moved.Destination, Has.Count.EqualTo (moved.Source.Count), "Source and Destination UID counts do not match");
				Assert.That (expunged, Is.EqualTo (21), "Expunged event");
				Assert.That (changed, Is.EqualTo (1), "CountChanged event");

				if (disableMove)
					client.Capabilities &= ~ImapCapabilities.Move;
				else
					client.Capabilities &= ~ImapCapabilities.UidPlus;

				expunged = changed = 0;

				moved = inbox.MoveTo (uids, archived);

				Assert.That (moved.Destination, Has.Count.EqualTo (moved.Source.Count), "Source and Destination UID counts do not match");
				Assert.That (expunged, Is.EqualTo (21), "Expunged event");
				Assert.That (changed, Is.EqualTo (1), "CountChanged event");

				client.Disconnect (true);
			}
		}

		[TestCase (true, TestName = "TestUidMoveToDisableMoveAsync")]
		[TestCase (false, TestName = "TestUidMoveToDisableUidPlusAsync")]
		public async Task TestUidMoveToAsync (bool disableMove)
		{
			var commands = CreateUidMoveToCommands (disableMove);

			using (var client = new ImapClient () { TagPrefix = 'A' }) {
				try {
					await client.ConnectAsync (new ImapReplayStream (commands, true), "localhost", 143, SecureSocketOptions.None);
				} catch (Exception ex) {
					Assert.Fail ($"Did not expect an exception in Connect: {ex}");
				}

				Assert.That (client.IsConnected, Is.True, "Client failed to connect.");

				try {
					await client.AuthenticateAsync ("username", "password");
				} catch (Exception ex) {
					Assert.Fail ($"Did not expect an exception in Authenticate: {ex}");
				}

				Assert.That (client.Capabilities.HasFlag (ImapCapabilities.UidPlus), Is.True, "Expected UIDPLUS extension");

				var personal = client.GetFolder (client.PersonalNamespaces [0]);
				var inbox = client.Inbox;

				await inbox.OpenAsync (FolderAccess.ReadWrite);
				var uids = await inbox.SearchAsync (SearchQuery.All);

				var archived = await personal.GetSubfolderAsync ("Archived Messages");
				int changed = 0, expunged = 0;

				inbox.MessageExpunged += (o, e) => { expunged++; Assert.That (e.Index, Is.EqualTo (0), "Expunged event message index"); };
				inbox.CountChanged += (o, e) => { changed++; };

				// Test moving using the MOVE & UIDPLUS extensions
				var moved = await inbox.MoveToAsync (uids, archived);

				Assert.That (moved.Destination, Has.Count.EqualTo (moved.Source.Count), "Source and Destination UID counts do not match");
				Assert.That (expunged, Is.EqualTo (21), "Expunged event");
				Assert.That (changed, Is.EqualTo (1), "CountChanged event");

				if (disableMove)
					client.Capabilities &= ~ImapCapabilities.Move;
				else
					client.Capabilities &= ~ImapCapabilities.UidPlus;

				expunged = changed = 0;

				moved = await inbox.MoveToAsync (uids, archived);

				Assert.That (moved.Destination, Has.Count.EqualTo (moved.Source.Count), "Source and Destination UID counts do not match");
				Assert.That (expunged, Is.EqualTo (21), "Expunged event");
				Assert.That (changed, Is.EqualTo (1), "CountChanged event");

				await client.DisconnectAsync (true);
			}
		}

		static List<ImapReplayCommand> CreateUidExpungeCommands (bool disableUidPlus)
		{
			var commands = new List<ImapReplayCommand> {
				new ImapReplayCommand ("", "gmail.greeting.txt"),
				new ImapReplayCommand ("A00000000 CAPABILITY\r\n", "gmail.capability.txt"),
				new ImapReplayCommand ("A00000001 AUTHENTICATE PLAIN AHVzZXJuYW1lAHBhc3N3b3Jk\r\n", "gmail.authenticate.txt"),
				new ImapReplayCommand ("A00000002 NAMESPACE\r\n", "gmail.namespace.txt"),
				new ImapReplayCommand ("A00000003 LIST \"\" \"INBOX\" RETURN (SUBSCRIBED CHILDREN)\r\n", "gmail.list-inbox.txt"),
				new ImapReplayCommand ("A00000004 XLIST \"\" \"*\"\r\n", "gmail.xlist.txt"),
				new ImapReplayCommand ("A00000005 SELECT INBOX (CONDSTORE)\r\n", "gmail.select-inbox.txt"),
				new ImapReplayCommand ("A00000006 UID SEARCH RETURN (ALL) ALL\r\n", "gmail.search.txt"),
				new ImapReplayCommand ("A00000007 UID STORE 1:3,5,7:9,11:14,26:29,31,34,41:43,50 +FLAGS.SILENT (\\Deleted)\r\n", ImapReplayCommandResponse.OK)
			};
			if (!disableUidPlus) {
				commands.Add (new ImapReplayCommand ("A00000008 UID EXPUNGE 1:3\r\n", "gmail.expunge.txt"));
				commands.Add (new ImapReplayCommand ("A00000009 LOGOUT\r\n", "gmail.logout.txt"));
			} else {
				commands.Add (new ImapReplayCommand ("A00000008 UID SEARCH RETURN (ALL) DELETED NOT UID 1:3\r\n", "gmail.search-deleted-not-1-3.txt"));
				commands.Add (new ImapReplayCommand ("A00000009 UID STORE 5,7:9,11:14,26:29,31,34,41:43,50 -FLAGS.SILENT (\\Deleted)\r\n", ImapReplayCommandResponse.OK));
				commands.Add (new ImapReplayCommand ("A00000010 EXPUNGE\r\n", "gmail.expunge.txt"));
				commands.Add (new ImapReplayCommand ("A00000011 UID STORE 5,7:9,11:14,26:29,31,34,41:43,50 +FLAGS.SILENT (\\Deleted)\r\n", ImapReplayCommandResponse.OK));
				commands.Add (new ImapReplayCommand ("A00000012 LOGOUT\r\n", "gmail.logout.txt"));
			}

			return commands;
		}

		[TestCase (false, TestName = "TestUidExpunge")]
		[TestCase (true, TestName = "TestUidExpungeDisableUidPlus")]
		public void TestUidExpunge (bool disableUidPlus)
		{
			var commands = CreateUidExpungeCommands (disableUidPlus);

			using (var client = new ImapClient () { TagPrefix = 'A' }) {
				try {
					client.Connect (new ImapReplayStream (commands, false), "localhost", 143, SecureSocketOptions.None);
				} catch (Exception ex) {
					Assert.Fail ($"Did not expect an exception in Connect: {ex}");
				}

				Assert.That (client.IsConnected, Is.True, "Client failed to connect.");

				try {
					client.Authenticate ("username", "password");
				} catch (Exception ex) {
					Assert.Fail ($"Did not expect an exception in Authenticate: {ex}");
				}

				int changed = 0, expunged = 0;
				var inbox = client.Inbox;

				inbox.Open (FolderAccess.ReadWrite);

				inbox.MessageExpunged += (o, e) => { expunged++; Assert.That (e.Index, Is.EqualTo (0), "Expunged event message index"); };
				inbox.CountChanged += (o, e) => { changed++; };

				var uids = inbox.Search (SearchQuery.All);
				inbox.AddFlags (uids, MessageFlags.Deleted, true);

				if (disableUidPlus)
					client.Capabilities &= ~ImapCapabilities.UidPlus;

				uids = new UniqueIdRange (0, 1, 3);
				inbox.Expunge (uids);

				Assert.That (expunged, Is.EqualTo (3), "Unexpected number of Expunged events");
				Assert.That (changed, Is.EqualTo (1), "Unexpected number of CountChanged events");
				Assert.That (inbox, Has.Count.EqualTo (18), "Count");

				client.Disconnect (true);
			}
		}

		[TestCase (false, TestName = "TestUidExpungeAsync")]
		[TestCase (true, TestName = "TestUidExpungeDisableUidPlusAsync")]
		public async Task TestUidExpungeAsync (bool disableUidPlus)
		{
			var commands = CreateUidExpungeCommands (disableUidPlus);

			using (var client = new ImapClient () { TagPrefix = 'A' }) {
				try {
					await client.ConnectAsync (new ImapReplayStream (commands, true), "localhost", 143, SecureSocketOptions.None);
				} catch (Exception ex) {
					Assert.Fail ($"Did not expect an exception in Connect: {ex}");
				}

				Assert.That (client.IsConnected, Is.True, "Client failed to connect.");

				try {
					await client.AuthenticateAsync ("username", "password");
				} catch (Exception ex) {
					Assert.Fail ($"Did not expect an exception in Authenticate: {ex}");
				}

				int changed = 0, expunged = 0;
				var inbox = client.Inbox;

				await inbox.OpenAsync (FolderAccess.ReadWrite);

				inbox.MessageExpunged += (o, e) => { expunged++; Assert.That (e.Index, Is.EqualTo (0), "Expunged event message index"); };
				inbox.CountChanged += (o, e) => { changed++; };

				var uids = await inbox.SearchAsync (SearchQuery.All);
				await inbox.AddFlagsAsync (uids, MessageFlags.Deleted, true);

				if (disableUidPlus)
					client.Capabilities &= ~ImapCapabilities.UidPlus;

				uids = new UniqueIdRange (0, 1, 3);
				await inbox.ExpungeAsync (uids);

				Assert.That (expunged, Is.EqualTo (3), "Unexpected number of Expunged events");
				Assert.That (changed, Is.EqualTo (1), "Unexpected number of CountChanged events");
				Assert.That (inbox, Has.Count.EqualTo (18), "Count");

				await client.DisconnectAsync (true);
			}
		}

		static List<ImapReplayCommand> CreateExplicitCountChangedCommands ()
		{
			return new List<ImapReplayCommand> {
				new ImapReplayCommand ("", "gmail.greeting.txt"),
				new ImapReplayCommand ("A00000000 CAPABILITY\r\n", "gmail.capability.txt"),
				new ImapReplayCommand ("A00000001 AUTHENTICATE PLAIN AHVzZXJuYW1lAHBhc3N3b3Jk\r\n", "gmail.authenticate.txt"),
				new ImapReplayCommand ("A00000002 NAMESPACE\r\n", "gmail.namespace.txt"),
				new ImapReplayCommand ("A00000003 LIST \"\" \"INBOX\" RETURN (SUBSCRIBED CHILDREN)\r\n", "gmail.list-inbox.txt"),
				new ImapReplayCommand ("A00000004 XLIST \"\" \"*\"\r\n", "gmail.xlist.txt"),
				// INBOX has 1 message present in this test
				new ImapReplayCommand ("A00000005 EXAMINE INBOX (CONDSTORE)\r\n", "gmail.count.examine.txt"),
				// The next response simulates an EXPUNGE notification followed by an explicit EXISTS notification.
				new ImapReplayCommand ("A00000006 NOOP\r\n", $"gmail.count-explicit.noop.txt"),
				new ImapReplayCommand ("A00000007 LOGOUT\r\n", "gmail.logout.txt")
			};
		}

		[Test]
		public void TestExplicitCountChanged ()
		{
			var commands = CreateExplicitCountChangedCommands ();

			using (var client = new ImapClient () { TagPrefix = 'A' }) {
				try {
					client.Connect (new ImapReplayStream (commands, false), "localhost", 143, SecureSocketOptions.None);
				} catch (Exception ex) {
					Assert.Fail ($"Did not expect an exception in Connect: {ex}");
				}

				Assert.That (client.IsConnected, Is.True, "Client failed to connect.");

				try {
					client.Authenticate ("username", "password");
				} catch (Exception ex) {
					Assert.Fail ($"Did not expect an exception in Authenticate: {ex}");
				}

				client.Inbox.Open (FolderAccess.ReadOnly);

				int messageExpungedEmitted = 0;
				int messageExpungedIndex = -1;
				int messageExpungedCount = -1;
				int countChangedEmitted = 0;
				int countChangedValue = -1;

				client.Inbox.CountChanged += delegate {
					countChangedValue = client.Inbox.Count;
					countChangedEmitted++;
				};

				client.Inbox.MessageExpunged += delegate (object sender, MessageEventArgs e) {
					messageExpungedCount = client.Inbox.Count;
					messageExpungedIndex = e.Index;
					messageExpungedEmitted++;
				};

				client.NoOp ();

				Assert.That (client.Inbox, Has.Count.EqualTo (1), "Count");
				Assert.That (countChangedEmitted, Is.EqualTo (1), "CountChanged was not emitted the expected number of times");
				Assert.That (countChangedValue, Is.EqualTo (1), "Count was not correct inside of the CountChanged event handler");

				Assert.That (messageExpungedIndex, Is.EqualTo (0), "The index of the expected message did not match");
				Assert.That (messageExpungedEmitted, Is.EqualTo (1), "MessageExpunged was not emitted the expected number of times");
				Assert.That (messageExpungedCount, Is.EqualTo (0), "Count was not correct inside of the MessageExpunged event handler");

				client.Disconnect (true);
			}
		}

		[Test]
		public async Task TestExplicitCountChangedAsync ()
		{
			var commands = CreateExplicitCountChangedCommands ();

			using (var client = new ImapClient () { TagPrefix = 'A' }) {
				try {
					await client.ConnectAsync (new ImapReplayStream (commands, true), "localhost", 143, SecureSocketOptions.None);
				} catch (Exception ex) {
					Assert.Fail ($"Did not expect an exception in Connect: {ex}");
				}

				Assert.That (client.IsConnected, Is.True, "Client failed to connect.");

				try {
					await client.AuthenticateAsync ("username", "password");
				} catch (Exception ex) {
					Assert.Fail ($"Did not expect an exception in Authenticate: {ex}");
				}

				await client.Inbox.OpenAsync (FolderAccess.ReadOnly);

				int messageExpungedEmitted = 0;
				int messageExpungedIndex = -1;
				int messageExpungedCount = -1;
				int countChangedEmitted = 0;
				int countChangedValue = -1;

				client.Inbox.CountChanged += delegate {
					countChangedValue = client.Inbox.Count;
					countChangedEmitted++;
				};

				client.Inbox.MessageExpunged += delegate (object sender, MessageEventArgs e) {
					messageExpungedCount = client.Inbox.Count;
					messageExpungedIndex = e.Index;
					messageExpungedEmitted++;
				};

				await client.NoOpAsync ();

				Assert.That (client.Inbox, Has.Count.EqualTo (1), "Count");
				Assert.That (countChangedEmitted, Is.EqualTo (1), "CountChanged was not emitted the expected number of times");
				Assert.That (countChangedValue, Is.EqualTo (1), "Count was not correct inside of the CountChanged event handler");

				Assert.That (messageExpungedIndex, Is.EqualTo (0), "The index of the expected message did not match");
				Assert.That (messageExpungedEmitted, Is.EqualTo (1), "MessageExpunged was not emitted the expected number of times");
				Assert.That (messageExpungedCount, Is.EqualTo (0), "Count was not correct inside of the MessageExpunged event handler");

				await client.DisconnectAsync (true);
			}
		}

		static List<ImapReplayCommand> CreateImplicitCountChangedCommands ()
		{
			return new List<ImapReplayCommand> {
				new ImapReplayCommand ("", "gmail.greeting.txt"),
				new ImapReplayCommand ("A00000000 CAPABILITY\r\n", "gmail.capability.txt"),
				new ImapReplayCommand ("A00000001 AUTHENTICATE PLAIN AHVzZXJuYW1lAHBhc3N3b3Jk\r\n", "gmail.authenticate.txt"),
				new ImapReplayCommand ("A00000002 NAMESPACE\r\n", "gmail.namespace.txt"),
				new ImapReplayCommand ("A00000003 LIST \"\" \"INBOX\" RETURN (SUBSCRIBED CHILDREN)\r\n", "gmail.list-inbox.txt"),
				new ImapReplayCommand ("A00000004 XLIST \"\" \"*\"\r\n", "gmail.xlist.txt"),
				// INBOX has 1 message present in this test
				new ImapReplayCommand ("A00000005 EXAMINE INBOX (CONDSTORE)\r\n", "gmail.count.examine.txt"),
				// The next response simulates an EXPUNGE notification without an explicit EXISTS notification.
				new ImapReplayCommand ("A00000006 NOOP\r\n", $"gmail.count-implicit.noop.txt"),
				new ImapReplayCommand ("A00000007 LOGOUT\r\n", "gmail.logout.txt")
			};
		}

		[Test]
		public void TestImplicitCountChanged ()
		{
			var commands = CreateImplicitCountChangedCommands ();

			using (var client = new ImapClient () { TagPrefix = 'A' }) {
				try {
					client.Connect (new ImapReplayStream (commands, false), "localhost", 143, SecureSocketOptions.None);
				} catch (Exception ex) {
					Assert.Fail ($"Did not expect an exception in Connect: {ex}");
				}

				Assert.That (client.IsConnected, Is.True, "Client failed to connect.");

				try {
					client.Authenticate ("username", "password");
				} catch (Exception ex) {
					Assert.Fail ($"Did not expect an exception in Authenticate: {ex}");
				}

				client.Inbox.Open (FolderAccess.ReadOnly);

				int messageExpungedEmitted = 0;
				int messageExpungedIndex = -1;
				int messageExpungedCount = -1;
				int countChangedEmitted = 0;
				int countChangedValue = -1;

				client.Inbox.CountChanged += delegate {
					countChangedValue = client.Inbox.Count;
					countChangedEmitted++;
				};

				client.Inbox.MessageExpunged += delegate (object sender, MessageEventArgs e) {
					messageExpungedCount = client.Inbox.Count;
					messageExpungedIndex = e.Index;
					messageExpungedEmitted++;
				};

				client.NoOp ();

				Assert.That (client.Inbox, Has.Count.EqualTo (0), "Count");
				Assert.That (countChangedEmitted, Is.EqualTo (1), "CountChanged was not emitted the expected number of times");
				Assert.That (countChangedValue, Is.EqualTo (0), "Count was not correct inside of the CountChanged event handler");

				Assert.That (messageExpungedIndex, Is.EqualTo (0), "The index of the expected message did not match");
				Assert.That (messageExpungedEmitted, Is.EqualTo (1), "MessageExpunged was not emitted the expected number of times");
				Assert.That (messageExpungedCount, Is.EqualTo (0), "Count was not correct inside of the MessageExpunged event handler");

				client.Disconnect (true);
			}
		}

		[Test]
		public async Task TestImplicitCountChangedAsync ()
		{
			var commands = CreateImplicitCountChangedCommands ();

			using (var client = new ImapClient () { TagPrefix = 'A' }) {
				try {
					await client.ConnectAsync (new ImapReplayStream (commands, true), "localhost", 143, SecureSocketOptions.None);
				} catch (Exception ex) {
					Assert.Fail ($"Did not expect an exception in Connect: {ex}");
				}

				Assert.That (client.IsConnected, Is.True, "Client failed to connect.");

				try {
					await client.AuthenticateAsync ("username", "password");
				} catch (Exception ex) {
					Assert.Fail ($"Did not expect an exception in Authenticate: {ex}");
				}

				await client.Inbox.OpenAsync (FolderAccess.ReadOnly);

				int messageExpungedEmitted = 0;
				int messageExpungedIndex = -1;
				int messageExpungedCount = -1;
				int countChangedEmitted = 0;
				int countChangedValue = -1;

				client.Inbox.CountChanged += delegate {
					countChangedValue = client.Inbox.Count;
					countChangedEmitted++;
				};

				client.Inbox.MessageExpunged += delegate (object sender, MessageEventArgs e) {
					messageExpungedCount = client.Inbox.Count;
					messageExpungedIndex = e.Index;
					messageExpungedEmitted++;
				};

				await client.NoOpAsync ();

				Assert.That (client.Inbox, Has.Count.EqualTo (0), "Count");
				Assert.That (countChangedEmitted, Is.EqualTo (1), "CountChanged was not emitted the expected number of times");
				Assert.That (countChangedValue, Is.EqualTo (0), "Count was not correct inside of the CountChanged event handler");

				Assert.That (messageExpungedIndex, Is.EqualTo (0), "The index of the expected message did not match");
				Assert.That (messageExpungedEmitted, Is.EqualTo (1), "MessageExpunged was not emitted the expected number of times");
				Assert.That (messageExpungedCount, Is.EqualTo (0), "Count was not correct inside of the MessageExpunged event handler");

				await client.DisconnectAsync (true);
			}
		}

		static void AssertFolder (IMailFolder folder, string fullName, FolderAttributes attributes, bool subscribed, ulong highestmodseq, int count, int recent, uint uidnext, uint validity, int unread)
		{
			if (subscribed)
				attributes |= FolderAttributes.Subscribed;

			Assert.That (folder.FullName, Is.EqualTo (fullName), "FullName");
			Assert.That (folder.Attributes, Is.EqualTo (attributes), "Attributes");
			Assert.That (folder.IsSubscribed, Is.EqualTo (subscribed), "IsSubscribed");
			Assert.That (folder.HighestModSeq, Is.EqualTo (highestmodseq), "HighestModSeq");
			Assert.That (folder, Has.Count.EqualTo (count), "Count");
			Assert.That (folder.Recent, Is.EqualTo (recent), "Recent");
			Assert.That (folder.Unread, Is.EqualTo (unread), "Unread");
			Assert.That (folder.UidNext.HasValue ? folder.UidNext.Value.Id : (uint)0, Is.EqualTo (uidnext), "UidNext");
			Assert.That (folder.UidValidity, Is.EqualTo (validity), "UidValidity");
		}

		static List<ImapReplayCommand> CreateGetSubfoldersWithStatusItemsCommands ()
		{
			return new List<ImapReplayCommand> {
				new ImapReplayCommand ("", "gmail.greeting.txt"),
				new ImapReplayCommand ("A00000000 CAPABILITY\r\n", "gmail.capability.txt"),
				new ImapReplayCommand ("A00000001 AUTHENTICATE PLAIN AHVzZXJuYW1lAHBhc3N3b3Jk\r\n", "gmail.authenticate.txt"),
				new ImapReplayCommand ("A00000002 NAMESPACE\r\n", "gmail.namespace.txt"),
				new ImapReplayCommand ("A00000003 LIST \"\" \"INBOX\" RETURN (SUBSCRIBED CHILDREN)\r\n", "gmail.list-inbox.txt"),
				new ImapReplayCommand ("A00000004 XLIST \"\" \"*\"\r\n", "gmail.xlist.txt"),
				//new ImapReplayCommand ("A00000005 LIST \"\" \"[Gmail]\"\r\n", "gmail.list-gmail.txt"),
				new ImapReplayCommand ("A00000005 LIST (SUBSCRIBED) \"\" \"[Gmail]/%\" RETURN (CHILDREN STATUS (MESSAGES RECENT UIDNEXT UIDVALIDITY UNSEEN HIGHESTMODSEQ))\r\n", "gmail.list-gmail-subfolders.txt"),
				new ImapReplayCommand ("A00000006 LIST \"\" \"[Gmail]/%\" RETURN (SUBSCRIBED CHILDREN)\r\n", "gmail.list-gmail-subfolders-no-status.txt"),
				new ImapReplayCommand ("A00000007 STATUS \"[Gmail]/All Mail\" (MESSAGES RECENT UIDNEXT UIDVALIDITY UNSEEN HIGHESTMODSEQ)\r\n", "gmail.status-all-mail.txt"),
				new ImapReplayCommand ("A00000008 STATUS \"[Gmail]/Drafts\" (MESSAGES RECENT UIDNEXT UIDVALIDITY UNSEEN HIGHESTMODSEQ)\r\n", "gmail.status-drafts.txt"),
				new ImapReplayCommand ("A00000009 STATUS \"[Gmail]/Important\" (MESSAGES RECENT UIDNEXT UIDVALIDITY UNSEEN HIGHESTMODSEQ)\r\n", "gmail.status-important.txt"),
				new ImapReplayCommand ("A00000010 STATUS \"[Gmail]/Sent Mail\" (MESSAGES RECENT UIDNEXT UIDVALIDITY UNSEEN HIGHESTMODSEQ)\r\n", "gmail.status-all-mail.txt"),
				new ImapReplayCommand ("A00000011 STATUS \"[Gmail]/Spam\" (MESSAGES RECENT UIDNEXT UIDVALIDITY UNSEEN HIGHESTMODSEQ)\r\n", "gmail.status-drafts.txt"),
				new ImapReplayCommand ("A00000012 STATUS \"[Gmail]/Starred\" (MESSAGES RECENT UIDNEXT UIDVALIDITY UNSEEN HIGHESTMODSEQ)\r\n", "gmail.status-important.txt"),
				new ImapReplayCommand ("A00000013 STATUS \"[Gmail]/Trash\" (MESSAGES RECENT UIDNEXT UIDVALIDITY UNSEEN HIGHESTMODSEQ)\r\n", "gmail.status-all-mail.txt"),
				new ImapReplayCommand ("A00000014 LOGOUT\r\n", "gmail.logout.txt")
			};
		}

		[Test]
		public void TestGetSubfoldersWithStatusItems ()
		{
			var commands = CreateGetSubfoldersWithStatusItemsCommands ();

			using (var client = new ImapClient () { TagPrefix = 'A' }) {
				try {
					client.Connect (new ImapReplayStream (commands, false), "localhost", 143, SecureSocketOptions.None);
				} catch (Exception ex) {
					Assert.Fail ($"Did not expect an exception in Connect: {ex}");
				}

				Assert.That (client.IsConnected, Is.True, "Client failed to connect.");

				try {
					client.Authenticate ("username", "password");
				} catch (Exception ex) {
					Assert.Fail ($"Did not expect an exception in Authenticate: {ex}");
				}

				var personal = client.GetFolder (client.PersonalNamespaces[0]);
				var gmail = personal.GetSubfolder ("[Gmail]");
				var all = StatusItems.Count | StatusItems.HighestModSeq | StatusItems.Recent | StatusItems.UidNext | StatusItems.UidValidity | StatusItems.Unread;
				var folders = gmail.GetSubfolders (all, true);
				Assert.That (folders, Has.Count.EqualTo (7), "Unexpected folder count.");

				AssertFolder (folders[0], "[Gmail]/All Mail", FolderAttributes.HasNoChildren | FolderAttributes.All, true, 41234, 67, 0, 1210, 11, 3);
				AssertFolder (folders[1], "[Gmail]/Drafts", FolderAttributes.HasNoChildren | FolderAttributes.Drafts, true, 41234, 0, 0, 1, 6, 0);
				AssertFolder (folders[2], "[Gmail]/Important", FolderAttributes.HasNoChildren | FolderAttributes.Important, true, 41234, 58, 0, 307, 9, 0);
				AssertFolder (folders[3], "[Gmail]/Sent Mail", FolderAttributes.HasNoChildren | FolderAttributes.Sent, true, 41234, 4, 0, 7, 5, 0);
				AssertFolder (folders[4], "[Gmail]/Spam", FolderAttributes.HasNoChildren | FolderAttributes.Junk, true, 41234, 0, 0, 1, 3, 0);
				AssertFolder (folders[5], "[Gmail]/Starred", FolderAttributes.HasNoChildren | FolderAttributes.Flagged, true, 41234, 1, 0, 7, 4, 0);
				AssertFolder (folders[6], "[Gmail]/Trash", FolderAttributes.HasNoChildren | FolderAttributes.Trash, true, 41234, 0, 0, 1143, 2, 0);

				AssertFolder (client.GetFolder (SpecialFolder.All), "[Gmail]/All Mail", FolderAttributes.HasNoChildren | FolderAttributes.All, true, 41234, 67, 0, 1210, 11, 3);
				AssertFolder (client.GetFolder (SpecialFolder.Drafts), "[Gmail]/Drafts", FolderAttributes.HasNoChildren | FolderAttributes.Drafts, true, 41234, 0, 0, 1, 6, 0);
				AssertFolder (client.GetFolder (SpecialFolder.Important), "[Gmail]/Important", FolderAttributes.HasNoChildren | FolderAttributes.Important, true, 41234, 58, 0, 307, 9, 0);
				AssertFolder (client.GetFolder (SpecialFolder.Sent), "[Gmail]/Sent Mail", FolderAttributes.HasNoChildren | FolderAttributes.Sent, true, 41234, 4, 0, 7, 5, 0);
				AssertFolder (client.GetFolder (SpecialFolder.Junk), "[Gmail]/Spam", FolderAttributes.HasNoChildren | FolderAttributes.Junk, true, 41234, 0, 0, 1, 3, 0);
				AssertFolder (client.GetFolder (SpecialFolder.Flagged), "[Gmail]/Starred", FolderAttributes.HasNoChildren | FolderAttributes.Flagged, true, 41234, 1, 0, 7, 4, 0);
				AssertFolder (client.GetFolder (SpecialFolder.Trash), "[Gmail]/Trash", FolderAttributes.HasNoChildren | FolderAttributes.Trash, true, 41234, 0, 0, 1143, 2, 0);

				// Now make the same query but disable LIST-STATUS
				client.Capabilities &= ~ImapCapabilities.ListStatus;
				folders = gmail.GetSubfolders (all, false);
				Assert.That (folders, Has.Count.EqualTo (7), "Unexpected folder count.");

				AssertFolder (folders[0], "[Gmail]/All Mail", FolderAttributes.HasNoChildren | FolderAttributes.All, true, 41234, 67, 0, 1210, 11, 3);
				AssertFolder (folders[1], "[Gmail]/Drafts", FolderAttributes.HasNoChildren | FolderAttributes.Drafts, true, 41234, 0, 0, 1, 6, 0);
				AssertFolder (folders[2], "[Gmail]/Important", FolderAttributes.HasNoChildren | FolderAttributes.Important | FolderAttributes.Marked, true, 41234, 58, 0, 307, 9, 0);
				AssertFolder (folders[3], "[Gmail]/Sent Mail", FolderAttributes.HasNoChildren | FolderAttributes.Sent | FolderAttributes.Unmarked, true, 41234, 4, 0, 7, 5, 0);
				AssertFolder (folders[4], "[Gmail]/Spam", FolderAttributes.HasNoChildren | FolderAttributes.Junk, true, 41234, 0, 0, 1, 3, 0);
				AssertFolder (folders[5], "[Gmail]/Starred", FolderAttributes.HasNoChildren | FolderAttributes.Flagged, true, 41234, 1, 0, 7, 4, 0);
				AssertFolder (folders[6], "[Gmail]/Trash", FolderAttributes.HasNoChildren | FolderAttributes.Trash, true, 41234, 0, 0, 1143, 2, 0);

				AssertFolder (client.GetFolder (SpecialFolder.All), "[Gmail]/All Mail", FolderAttributes.HasNoChildren | FolderAttributes.All, true, 41234, 67, 0, 1210, 11, 3);
				AssertFolder (client.GetFolder (SpecialFolder.Drafts), "[Gmail]/Drafts", FolderAttributes.HasNoChildren | FolderAttributes.Drafts, true, 41234, 0, 0, 1, 6, 0);
				AssertFolder (client.GetFolder (SpecialFolder.Important), "[Gmail]/Important", FolderAttributes.HasNoChildren | FolderAttributes.Important | FolderAttributes.Marked, true, 41234, 58, 0, 307, 9, 0);
				AssertFolder (client.GetFolder (SpecialFolder.Sent), "[Gmail]/Sent Mail", FolderAttributes.HasNoChildren | FolderAttributes.Sent | FolderAttributes.Unmarked, true, 41234, 4, 0, 7, 5, 0);
				AssertFolder (client.GetFolder (SpecialFolder.Junk), "[Gmail]/Spam", FolderAttributes.HasNoChildren | FolderAttributes.Junk, true, 41234, 0, 0, 1, 3, 0);
				AssertFolder (client.GetFolder (SpecialFolder.Flagged), "[Gmail]/Starred", FolderAttributes.HasNoChildren | FolderAttributes.Flagged, true, 41234, 1, 0, 7, 4, 0);
				AssertFolder (client.GetFolder (SpecialFolder.Trash), "[Gmail]/Trash", FolderAttributes.HasNoChildren | FolderAttributes.Trash, true, 41234, 0, 0, 1143, 2, 0);

				client.Disconnect (true);
			}
		}

		[Test]
		public async Task TestGetSubfoldersWithStatusItemsAsync ()
		{
			var commands = CreateGetSubfoldersWithStatusItemsCommands ();

			using (var client = new ImapClient () { TagPrefix = 'A' }) {
				try {
					await client.ConnectAsync (new ImapReplayStream (commands, true), "localhost", 143, SecureSocketOptions.None);
				} catch (Exception ex) {
					Assert.Fail ($"Did not expect an exception in Connect: {ex}");
				}

				try {
					await client.AuthenticateAsync ("username", "password");
				} catch (Exception ex) {
					Assert.Fail ($"Did not expect an exception in Authenticate: {ex}");
				}

				var personal = client.GetFolder (client.PersonalNamespaces[0]);
				var gmail = await personal.GetSubfolderAsync ("[Gmail]");
				var all = StatusItems.Count | StatusItems.HighestModSeq | StatusItems.Recent | StatusItems.UidNext | StatusItems.UidValidity | StatusItems.Unread;
				var folders = await gmail.GetSubfoldersAsync (all, true);
				Assert.That (folders, Has.Count.EqualTo (7), "Unexpected folder count.");

				AssertFolder (folders[0], "[Gmail]/All Mail", FolderAttributes.HasNoChildren | FolderAttributes.All, true, 41234, 67, 0, 1210, 11, 3);
				AssertFolder (folders[1], "[Gmail]/Drafts", FolderAttributes.HasNoChildren | FolderAttributes.Drafts, true, 41234, 0, 0, 1, 6, 0);
				AssertFolder (folders[2], "[Gmail]/Important", FolderAttributes.HasNoChildren | FolderAttributes.Important, true, 41234, 58, 0, 307, 9, 0);
				AssertFolder (folders[3], "[Gmail]/Sent Mail", FolderAttributes.HasNoChildren | FolderAttributes.Sent, true, 41234, 4, 0, 7, 5, 0);
				AssertFolder (folders[4], "[Gmail]/Spam", FolderAttributes.HasNoChildren | FolderAttributes.Junk, true, 41234, 0, 0, 1, 3, 0);
				AssertFolder (folders[5], "[Gmail]/Starred", FolderAttributes.HasNoChildren | FolderAttributes.Flagged, true, 41234, 1, 0, 7, 4, 0);
				AssertFolder (folders[6], "[Gmail]/Trash", FolderAttributes.HasNoChildren | FolderAttributes.Trash, true, 41234, 0, 0, 1143, 2, 0);

				AssertFolder (client.GetFolder (SpecialFolder.All), "[Gmail]/All Mail", FolderAttributes.HasNoChildren | FolderAttributes.All, true, 41234, 67, 0, 1210, 11, 3);
				AssertFolder (client.GetFolder (SpecialFolder.Drafts), "[Gmail]/Drafts", FolderAttributes.HasNoChildren | FolderAttributes.Drafts, true, 41234, 0, 0, 1, 6, 0);
				AssertFolder (client.GetFolder (SpecialFolder.Important), "[Gmail]/Important", FolderAttributes.HasNoChildren | FolderAttributes.Important, true, 41234, 58, 0, 307, 9, 0);
				AssertFolder (client.GetFolder (SpecialFolder.Sent), "[Gmail]/Sent Mail", FolderAttributes.HasNoChildren | FolderAttributes.Sent, true, 41234, 4, 0, 7, 5, 0);
				AssertFolder (client.GetFolder (SpecialFolder.Junk), "[Gmail]/Spam", FolderAttributes.HasNoChildren | FolderAttributes.Junk, true, 41234, 0, 0, 1, 3, 0);
				AssertFolder (client.GetFolder (SpecialFolder.Flagged), "[Gmail]/Starred", FolderAttributes.HasNoChildren | FolderAttributes.Flagged, true, 41234, 1, 0, 7, 4, 0);
				AssertFolder (client.GetFolder (SpecialFolder.Trash), "[Gmail]/Trash", FolderAttributes.HasNoChildren | FolderAttributes.Trash, true, 41234, 0, 0, 1143, 2, 0);

				// Now make the same query but disable LIST-STATUS
				client.Capabilities &= ~ImapCapabilities.ListStatus;
				folders = await gmail.GetSubfoldersAsync (all, false);
				Assert.That (folders, Has.Count.EqualTo (7), "Unexpected folder count.");

				AssertFolder (folders[0], "[Gmail]/All Mail", FolderAttributes.HasNoChildren | FolderAttributes.All, true, 41234, 67, 0, 1210, 11, 3);
				AssertFolder (folders[1], "[Gmail]/Drafts", FolderAttributes.HasNoChildren | FolderAttributes.Drafts, true, 41234, 0, 0, 1, 6, 0);
				AssertFolder (folders[2], "[Gmail]/Important", FolderAttributes.HasNoChildren | FolderAttributes.Important | FolderAttributes.Marked, true, 41234, 58, 0, 307, 9, 0);
				AssertFolder (folders[3], "[Gmail]/Sent Mail", FolderAttributes.HasNoChildren | FolderAttributes.Sent | FolderAttributes.Unmarked, true, 41234, 4, 0, 7, 5, 0);
				AssertFolder (folders[4], "[Gmail]/Spam", FolderAttributes.HasNoChildren | FolderAttributes.Junk, true, 41234, 0, 0, 1, 3, 0);
				AssertFolder (folders[5], "[Gmail]/Starred", FolderAttributes.HasNoChildren | FolderAttributes.Flagged, true, 41234, 1, 0, 7, 4, 0);
				AssertFolder (folders[6], "[Gmail]/Trash", FolderAttributes.HasNoChildren | FolderAttributes.Trash, true, 41234, 0, 0, 1143, 2, 0);

				AssertFolder (client.GetFolder (SpecialFolder.All), "[Gmail]/All Mail", FolderAttributes.HasNoChildren | FolderAttributes.All, true, 41234, 67, 0, 1210, 11, 3);
				AssertFolder (client.GetFolder (SpecialFolder.Drafts), "[Gmail]/Drafts", FolderAttributes.HasNoChildren | FolderAttributes.Drafts, true, 41234, 0, 0, 1, 6, 0);
				AssertFolder (client.GetFolder (SpecialFolder.Important), "[Gmail]/Important", FolderAttributes.HasNoChildren | FolderAttributes.Important | FolderAttributes.Marked, true, 41234, 58, 0, 307, 9, 0);
				AssertFolder (client.GetFolder (SpecialFolder.Sent), "[Gmail]/Sent Mail", FolderAttributes.HasNoChildren | FolderAttributes.Sent | FolderAttributes.Unmarked, true, 41234, 4, 0, 7, 5, 0);
				AssertFolder (client.GetFolder (SpecialFolder.Junk), "[Gmail]/Spam", FolderAttributes.HasNoChildren | FolderAttributes.Junk, true, 41234, 0, 0, 1, 3, 0);
				AssertFolder (client.GetFolder (SpecialFolder.Flagged), "[Gmail]/Starred", FolderAttributes.HasNoChildren | FolderAttributes.Flagged, true, 41234, 1, 0, 7, 4, 0);
				AssertFolder (client.GetFolder (SpecialFolder.Trash), "[Gmail]/Trash", FolderAttributes.HasNoChildren | FolderAttributes.Trash, true, 41234, 0, 0, 1143, 2, 0);

				await client.DisconnectAsync (true);
			}
		}
	}
}
