using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading;

namespace RIP
{

    class Program
    {
        //implementation of RIPv1
        
        static void Main(string[] args)
        {
			
			var routers = new List<Router>();
			for(int i = 0; i < 5; i++){
				routers.Add(Router.CreateInstance());
			}
			foreach(var router in routers)
			{
				Console.WriteLine(router.OwnIP);
			}

			Router.SetNeighbors(routers, "1-2, 5-4, 3-2, 5-1");

			var tasks = new Task[routers.Count];
			int index = 0;
			foreach(var router in routers)
			{
				Console.WriteLine("Router: " + router.OwnIP);
				Console.WriteLine("Neighbors: ");
				router.Neighbors.ForEach(x => Console.Write(x.Ip+", "));
				Console.WriteLine();
				tasks[index] = Task.Run(() => router.StartRouting());
				index++;
				Thread.Sleep(200);
			}
			
			
			//add event loop that listens for input for a few commands:
			//start: a router, check if tables update
			//stop: a router, check how it propagates through the network
			//print: routing table of a selected router
			//quit
			//Thread.Sleep(2000);
			//routers[4].Active = false;
			var input = "";
			while(input != "quit")
			{
				input = Console.ReadLine();
				var parameters = input.Split(null);
				var command = parameters[0];
				if(parameters.Length < 2) continue;
				var parseSuccess = int.TryParse(parameters[1], out int argument);
				if(!parseSuccess) continue;
				if(argument > 0 && argument <= 5)
				{
					argument -= 1;
				}
				else continue;

				switch(command){
					case "start":
						if(routers[argument].Active) break;
						tasks[argument] = Task.Run(() => routers[argument].StartRouting());
						break;
					case "stop":
						routers[argument].Active = false;
						break;
					case "print":
						routers[argument].PrintRoutingTable();
						break;
					default:
						break;
				}
			}
			foreach(var router in routers)
			{
				router.Active = false;
			}
			
			Task.WhenAll(tasks).Wait();
			
        }
    }

	
}
