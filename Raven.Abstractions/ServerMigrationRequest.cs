﻿// -----------------------------------------------------------------------
//  <copyright file="ServerMigrationRequest.cs" company="Hibernating Rhinos LTD">
//      Copyright (c) Hibernating Rhinos LTD. All rights reserved.
//  </copyright>
// -----------------------------------------------------------------------
using System.Collections.Generic;
using System.Net;

namespace Raven.Abstractions
{
	public class ServerMigrationRequest
	{
		public ServerConnectionInfo TargetServer { get; set; }
		public List<ServerMigrationItem> Config { get; set; }
	}

	public class ServerConnectionInfo
	{
		public string Url { get; set; }
		public string Username { get; set; }
		public string Password { get; set; }
		public string Domain { get; set; }
		public string ApiKey { get; set; }

		public ICredentials Credentials
		{
			get
			{
				return string.IsNullOrEmpty(Username) == false ? new NetworkCredential(Username, Password, Domain ?? string.Empty) : null;
			}
		}
	}

	public class ServerMigrationItem
	{
		public string Name { get; set; }
	}

}