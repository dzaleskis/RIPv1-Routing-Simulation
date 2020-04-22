using System;
using System.Net;
using System.Net.Sockets;
using System.Collections.Generic;

namespace RIP
{
    internal class RouteEntry : IEquatable<RouteEntry>
	{
		public uint Ip {get;set;}
		public uint Gateway {get;set;}
		public uint Cost {get;set;}

        public bool Equals(RouteEntry other)
        {
			if(other == null) return false;
			return this.Ip == other.Ip;
        	//return (this.Ip == other.Ip && this.Gateway == other.Gateway && this.Cost == other.Cost);
        }

		public override bool Equals(object obj)
		{
			if (obj == null) return false;
			RouteEntry objAsRt = obj as RouteEntry;
			if (objAsRt == null) return false;
			else return Equals(objAsRt);
		}
		public override string ToString(){
			return $"Ip: {Router.ConvertFromIntegerToIpAddress(Ip).ToString()} Gateway: {Router.ConvertFromIntegerToIpAddress(Gateway).ToString()} Cost: {Cost}";
		}
    }

	internal struct Neighbor{
		public IPAddress Ip {get;set;}
		public int port {get;set;}
	}

    class Router
	{
        readonly TimeSpan UpdateInterval = TimeSpan.FromMilliseconds(300);
		readonly TimeSpan ExpirationInterval = TimeSpan.FromMilliseconds(1000);
        readonly TimeSpan RemovalInterval = TimeSpan.FromMilliseconds(1400);
        readonly int OwnPort;
		readonly static int maxRouters = 20;
		public static int defaultPort = 8080;
		public static int lowestAvailablePort = defaultPort;
        const int MaxHopCount = 15;
		private static int activeInstances = 0;
		public IPAddress OwnIP {get;set;}
		public bool Active {get;set;}
		public ExpirableList<RouteEntry> RouteTable {get;set;}
		public List<Neighbor> Neighbors {get;set;}

		public static void SetNeighbors(List<Router> routers, string connectionInstructions)
		{
			//take in a string with instructions like so:
			//1-2, 5-8,
			//......
			//and add the corresponding neighbors to the routers
			var pairs = connectionInstructions.Split(", ");
			foreach(var pair in pairs)
			{
				Console.WriteLine(pair);
				string[] elements = pair.Split('-'); 
				var parsed = int.TryParse(elements[0], out int firstIndex);
				parsed = int.TryParse(elements[1], out int secondIndex);

				var first = routers[firstIndex - 1];
				var second = routers[secondIndex - 1];
				first.SetNeighbor(second.OwnIP, second.OwnPort);
				second.SetNeighbor(first.OwnIP, first.OwnPort);
			}

		}

		private void SetNeighbor(IPAddress neighborIp, int neighborPort)
		{
			Neighbors.Add(new Neighbor(){
				Ip = neighborIp,
				port = neighborPort
			});
		}
		
		private Router(IPAddress assignedIP, int assignedPort)
		{
			RouteTable = new ExpirableList<RouteEntry>(300, RemovalInterval);
			Neighbors = new List<Neighbor>();
			OwnIP = assignedIP;
			OwnPort = assignedPort;
		}

		public static Router CreateInstance()
		{
			if(activeInstances >= maxRouters){
				Console.WriteLine("too many active instances");
				return null;
			}
			var assignedIP = "192.168.0." + ((lowestAvailablePort % maxRouters) + 1);
			var router = new Router(IPAddress.Parse(assignedIP), lowestAvailablePort);
			lowestAvailablePort++;
			activeInstances++;
			return router;
		}

		public void StartRouting()
		{
			
			Active = true;
			var updateTimer = new System.Threading.Timer((e) =>
			{
				if(Active)
				{
					SendRequestToNeighbors();
				}
				
			}, null, TimeSpan.Zero, UpdateInterval);

			var expirationTimer = new System.Threading.Timer((e) =>
			{
				if(Active)
				{
					InvalidateExpired();
					InvalidateUnreachable();
				}
				
			}, null, TimeSpan.Zero, ExpirationInterval);

			
			Console.WriteLine("started routing");
			Listen();

			updateTimer.Dispose();
			expirationTimer.Dispose();

			Console.WriteLine("stopped routing");
		}

		private void SendRequestToNeighbors()
		{
			var request = CreateRequest();
			var requestSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
			requestSocket.SendTimeout = UpdateInterval.Milliseconds / 2;
			foreach(var neighbor in Neighbors)
			{
				try
				{
					requestSocket.SendTo(request, new IPEndPoint(IPAddress.Loopback, neighbor.port));
				}
				catch(SocketException e)
				{
					if(e.ErrorCode != 110){
						Console.WriteLine(e.Message);
					}
				}
			}
			requestSocket.Close();
		}

		private void Listen()
		{
			var receiveSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
			receiveSocket.ReceiveTimeout = UpdateInterval.Milliseconds + 200;
			var responseSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
			responseSocket.SendTimeout = UpdateInterval.Milliseconds / 2;
			receiveSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
			receiveSocket.Bind(new IPEndPoint(IPAddress.Loopback, OwnPort));
			
			while(Active)
			{
				var received = 0;
				var receiveBuffer = new byte[1024];

				try
				{
					received = receiveSocket.Receive(receiveBuffer);	
				}
				catch(SocketException e)
				{
					if(e.ErrorCode != 110){
						Console.WriteLine(e.Message);
					}
					
				}
				if(received <= 0) continue;

				//received request
				if(receiveBuffer[0] == 1)
				{
					var ip = ConvertFromBytesToInteger(receiveBuffer, 8);
					var cost = ConvertFromBytesToInteger(receiveBuffer, 20);
					IPAddress ipAddress = IPAddress.Parse(ip.ToString());
					var neighbor = Neighbors.Find(n => n.Ip.Equals(ipAddress));
					var response = CreateResponse();
					try
					{
						responseSocket.SendTo(response, new IPEndPoint(IPAddress.Loopback, neighbor.port));	
					}
					catch(SocketException e)
					{
						if(e.ErrorCode != 110){
							Console.WriteLine(e.Message);
						}
						
					}
				}
				//received response
				if(receiveBuffer[0] == 2)
				{
					UpdateRoutes(receiveBuffer, received);
				}
			}
		}


		private byte[] CreateRequest()
		{
			var request = new byte[24];
			request[0] = 1; // packet type - request
			request[1] = 1; // version number - v1
			// bytes [2,3] are left empty
			// bytes [4,5] are address family, since we want the whole routing table, leave empty
			// bytes [6,7] are left empty
			OwnIP.GetAddressBytes().CopyTo(request, 8); // so routers can identify each other by ip, since 
			//we are running on loopback
			//next eight bytes are left empty
			//following 4 bytes are metric, since max hop count is 15, set it to infinity(16)
			BitConverter.GetBytes(MaxHopCount + 1).CopyTo(request, 20);
			return request;
		}

		public void PrintRoutingTable()
		{
			Console.WriteLine($"Router: {OwnIP.ToString()} Route entries: {RouteTable.Count}");
			RouteTable.PrintItems();
		}

		private byte[] CreateResponse()
		{
			var responseHeader = new byte[24];
			responseHeader[0] = 2; // packet type - response
			responseHeader[1] = 1; // version number - v1
			// next 2 bytes are left empty
			// bytes [4,5] are address family
			responseHeader[5] = 2; //ip address family is represented by 2
			// next 2 bytes are left empty
			OwnIP.GetAddressBytes().CopyTo(responseHeader, 8); // so routers can identify each other by ip, since 
			//we are running on loopback
			//next eight bytes are left empty
			//following 4 bytes are metric, since the router is a neighbor, cost is 0
			//BitConverter.GetBytes(0).CopyTo(responseHeader, 20);

			//every entry in routing table takes up 20 bytes
			var response = new byte[responseHeader.Length + (RouteTable.Count*20)];
			responseHeader.CopyTo(response, 0);
			var currentIndex = responseHeader.Length;
			RouteTable.mutex.WaitOne();
			foreach(var entry in RouteTable)
			{
				//first 2 bytes are address family
				//ip address family is represented by 2
				response[currentIndex+1] = 2;
				// next 2 bytes are left empty
				// then 4 bytes are ip address of entry
				BitConverter.GetBytes(ConvertToNetworkOrder(entry.Ip)).CopyTo(response, currentIndex+4);
				//next 8 bytes are left empty
				//then 4 bytes are metric
				BitConverter.GetBytes(ConvertToNetworkOrder(entry.Cost)).CopyTo(response, currentIndex+16);
				currentIndex += 20;
			}
			RouteTable.mutex.ReleaseMutex();
			return response;
		}

		private void UpdateRoutes(byte[] response, int bytesReceived)
		{
			var neighbor = new RouteEntry(){
				Ip = ConvertFromBytesToInteger(response, 8),
				Gateway = ConvertFromIpAddressToInteger(OwnIP),
				Cost = 0
			};
			
			var routes = new List<RouteEntry>();
			routes.Add(neighbor);

			if(bytesReceived > 24)
			{
				for(int index = 24; index < bytesReceived; index += 20)
				{
					var entry = new RouteEntry(){
						Ip = ConvertFromBytesToInteger(response, index+4),
						Gateway = neighbor.Ip,
						Cost = ConvertFromBytesToInteger(response, index+16) + 1
					};
					
					//dont add yourself to route list
					if(ConvertFromIntegerToIpAddress(entry.Ip).Equals(OwnIP)) continue;
					//dont add routes with hop count higher than 15
					if(entry.Cost > MaxHopCount) continue;

					routes.Add(entry);
				}
			}
			RouteTable.mutex.WaitOne();
            foreach(var entry in routes)
            {
                if(RouteTable.Contains(entry))
				{
					int index = RouteTable.IndexOf(entry);
					var oldEntry = RouteTable[index];
					if(oldEntry.Cost >= entry.Cost)
					{
						RouteTable[index] = entry;
					}
				}
				else
				{
					RouteTable.Add(entry);
				}
            }
			RouteTable.mutex.ReleaseMutex();
		}

		private void InvalidateExpired()
		{
			RouteTable.mutex.WaitOne();
			for(int i = 0; i < RouteTable.Count; i++)
			{
				if(DateTime.Now - RouteTable[i, true].Item1 > ExpirationInterval)
				{
					RouteTable[i].Cost = MaxHopCount + 1;
				}
			}
			RouteTable.mutex.ReleaseMutex();
		}

		private void InvalidateUnreachable(){

			RouteTable.mutex.WaitOne();
			var routeList = RouteTable.AsNormalList();
			for(int i = 0; i < RouteTable.Count; i++)
			{
				var entry = RouteTable[i];
				bool gatewayValid = false;
				var ownIpConverted = ConvertFromIpAddressToInteger(OwnIP);
				foreach(var e in routeList){
					
					if(entry.Gateway == e.Ip && e.Cost <= MaxHopCount)
					{
						gatewayValid = true;
					}
					if(entry.Gateway == ownIpConverted)
					{
						gatewayValid = true;
					}
				}
				if(!gatewayValid)
				{
					entry.Cost = MaxHopCount + 1;
				}
			}
			RouteTable.mutex.ReleaseMutex();
		}

		public static uint ConvertToNetworkOrder(uint value)
		{
			return (uint) IPAddress.HostToNetworkOrder((int) value);
		}

		public static int ConvertToNetworkOrder(int value)
		{
			return IPAddress.HostToNetworkOrder(value);
		}

		public static uint ConvertFromBytesToInteger(byte[] array, int index)
		{
			var subarray = new byte[4];
			Array.Copy(array, index, subarray, 0, 4);

			// flip big-endian(network order) to little-endian
			if (BitConverter.IsLittleEndian)
			{
				Array.Reverse(subarray);
			}

			return BitConverter.ToUInt32(subarray, 0);
		}
		public static uint ConvertFromIpAddressToInteger(IPAddress ipAddress)
		{
			//var address = IPAddress.Parse(ipAddress);
			byte[] bytes = ipAddress.GetAddressBytes();

			// flip big-endian(network order) to little-endian
			if (BitConverter.IsLittleEndian)
			{
				Array.Reverse(bytes);
			}

			return BitConverter.ToUInt32(bytes, 0);
		}

		public static IPAddress ConvertFromIntegerToIpAddress(uint ipAddress)
		{
			byte[] bytes = BitConverter.GetBytes(ipAddress);

			// flip little-endian to big-endian(network order)
			if (BitConverter.IsLittleEndian)
			{
				Array.Reverse(bytes);
			}

			return new IPAddress(bytes);
		}
		
	}
}