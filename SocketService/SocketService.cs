using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Linq;
using System.ServiceProcess;
using System.Text;

namespace SocketService
{
    public partial class SocketService : ServiceBase
    {
        static readonly object _broadcastLock = new object();

        private IList<Fleck.IWebSocketConnection> _connections = new List<Fleck.IWebSocketConnection>();
        private System.Diagnostics.EventLog _eventLog = null;

        private System.Collections.Generic.Queue<BufferItem> _broadcastBuffer = new Queue<BufferItem>();
        private System.DateTime _broadcastTimestamp;
       
        private Fleck.WebSocketServer _server;

        private Boolean _isPaused = false;

        private struct BufferItem
        {
            public System.Guid ConnectionId;
            public System.DateTime TimeStamp;
            public System.String Message;

            public BufferItem(System.Guid connectionId, System.String message)
            {
                this.ConnectionId = connectionId;
                this.Message = message;

                this.TimeStamp = DateTime.UtcNow;
            }
        }

        public SocketService()
        {
            InitializeComponent();

            this._eventLog = new System.Diagnostics.EventLog();
            this._eventLog.Source = this.ServiceName;
            this._eventLog.Log = "Application";

            ((System.ComponentModel.ISupportInitialize)(this.EventLog)).BeginInit();
            if (!System.Diagnostics.EventLog.SourceExists(this.EventLog.Source))
            {
                System.Diagnostics.EventLog.CreateEventSource(this.EventLog.Source, this.EventLog.Log);
            }
            ((System.ComponentModel.ISupportInitialize)(this.EventLog)).EndInit();

            this._broadcastTimestamp = DateTime.UtcNow;
        }

        protected override void OnStart(string[] args)
        {
            this._connections.Clear();

            this._isPaused = false;

            Int32 port;
            if( System.Int32.TryParse( System.Configuration.ConfigurationSettings.AppSettings["Port"], out port) == false ) port = 8181;
            
            Fleck.FleckLog.Level = Fleck.LogLevel.Error;

            if(System.Diagnostics.EventLog.SourceExists(this.ServiceName) == true)
                this._eventLog.WriteEntry("SocketService is listening to port " + port, EventLogEntryType.Information);
            
            this._server = new Fleck.WebSocketServer(port, "ws://localhost/");
            this._server.Start(socket =>
            {
                socket.OnOpen = () =>
                    {
                        if(System.Diagnostics.EventLog.SourceExists(this.ServiceName) == true)
                            this._eventLog.WriteEntry("Client '" + socket.ConnectionInfo.Host + "' connected (" + socket.ConnectionInfo.Id + ", '" + socket.ConnectionInfo.Path + "').", EventLogEntryType.Information);

                        this._connections.Add(socket);
                    };
                socket.OnClose = () =>
                    {
                        if(System.Diagnostics.EventLog.SourceExists(this.ServiceName) == true)
                            this._eventLog.WriteEntry("Client '" + socket.ConnectionInfo.Host + "' disconnected (" + socket.ConnectionInfo.Id + ").", EventLogEntryType.Information);

                        this._connections.Remove(socket);
                    };
                socket.OnError = exception =>
                    {
                        if( exception is System.IO.IOException )
                        {
                            if(System.Diagnostics.EventLog.SourceExists(this.ServiceName) == true)
                                this._eventLog.WriteEntry("Client '" + socket.ConnectionInfo.Host + "' disconnected with an abnormal behaviour (" + socket.ConnectionInfo.Id + ").", EventLogEntryType.Information);
                        } 
                        else 
                        {
                            if(System.Diagnostics.EventLog.SourceExists(this.ServiceName) == true)
                                this.EventLog.WriteEntry(exception.GetType().ToString() + "\n" + exception.Message + "\n\n" + exception.StackTrace, EventLogEntryType.Error);
                        }
                    };
                socket.OnMessage = message =>
                    {
                        if(this._isPaused == true) return;

                        this._broadcastBuffer.Enqueue(new BufferItem(socket.ConnectionInfo.Id, message));

                        (new System.Threading.Thread(this.BroadcastMessage)).Start();
                    };
            });
        }

        public void BroadcastMessage()
        {
            lock(SocketService._broadcastLock)
            {
                Fleck.IWebSocketConnection[] connections;
                System.Collections.Generic.Stack<Fleck.IWebSocketConnection> unavailable = null;

                this._connections.CopyTo(connections = new Fleck.IWebSocketConnection[this._connections.Count], 0);

                BufferItem item;
                while(this._broadcastBuffer.Count > 0)
                {
                    item = this._broadcastBuffer.Dequeue();

                    if (DateTime.UtcNow.Subtract(item.TimeStamp).Milliseconds > 10000) 
                        continue;

                    if(item.Message != null && item.Message.Length > 0)
                        foreach (Fleck.IWebSocketConnection client in connections)
                        {
                            if (client.IsAvailable == true)
                            {
                                if (client.ConnectionInfo.Path.Equals("/Listener") == true)
                                {
                                    if (item.ConnectionId.Equals(client.ConnectionInfo.Id) == false)
                                        client.Send(item.Message);
                                }
                            }
                            else
                            {
                                unavailable = new Stack<Fleck.IWebSocketConnection>();
                                unavailable.Push(client);
                            }
                        }
                }

                if (unavailable != null)
                {
                    while (unavailable.Count != 0) this._connections.Remove(unavailable.Pop());
                }

                this._broadcastTimestamp = DateTime.UtcNow;
            }
        }

        protected override void OnPause()
        {
            this._isPaused = true;
        }

        protected override void OnContinue()
        {
            this._isPaused = false;
        }
        
        protected override void OnStop()
        {
            this._isPaused = false;

            if(this._server != null) 
            {
                foreach(Fleck.IWebSocketConnection client in this._connections)
                    if(client != null && client.IsAvailable == true) client.Close();

                this._server.Dispose();
                this._server = null;
            }

            if(this._connections != null) 
                this._connections.Clear();
            
            this._connections = null;

            if(this._eventLog != null)
                this._eventLog.Dispose();
        }
    }
}
