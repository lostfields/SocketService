using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Fleck;

namespace SocketServiceDebug
{
    class Program
    {
        static void Main(string[] args)
        {
            
            FleckLog.Level = LogLevel.Debug;
            var allSockets = new List<IWebSocketConnection>();
            WebSocketServer server;

            Int32 port;
            if( System.Int32.TryParse( System.Configuration.ConfigurationSettings.AppSettings["Port"], out port) == false ) port = 8181;
            
            
            server = new WebSocketServer(8181, "ws://localhost");
           
            server.Start(socket =>
                {
                    socket.OnOpen = () =>
                        {
                            Console.WriteLine("[" + socket.ConnectionInfo.Host + "] connected");
                            allSockets.Add(socket);
                        };
                    socket.OnClose = () =>
                        {
                            Console.WriteLine("[" + socket.ConnectionInfo.Host + "] closed connection");
                            allSockets.Remove(socket);
                        };
                    socket.OnError = exception =>
                    {
                        if( exception is System.IO.IOException )
                        {
                            Console.WriteLine("[" + socket.ConnectionInfo.Host + "] closed connection");
                            
                        }
                    };
                    socket.OnMessage = message =>
                        {
                            try {
                                
                                //socket.ConnectionInfo.Id

                                foreach(IWebSocketConnection client in allSockets)
                                {
                                    if(client.IsAvailable == true)
                                    {
                                        if( client.ConnectionInfo.Path.Equals("/Listener") == true)
                                        {
                                            if(socket.ConnectionInfo.Id.Equals(client.ConnectionInfo.Id) == false)
                                                client.Send(message);
                                        }
                                    } else {
                                        allSockets.Remove( client );
                                    }
                                }

                                //Console.WriteLine(message);
                                //allSockets.ToList().ForEach(s => s.Send("Echo: " + message));
                            } 
                            catch(System.Exception)
                            {

                            }
                        };
                });


            var input = Console.ReadLine();
            while (input != "exit")
            {
                foreach (var socket in allSockets.ToList())
                {
                    socket.Send(input);
                }
                input = Console.ReadLine();
            }

      
        }
    }
}
