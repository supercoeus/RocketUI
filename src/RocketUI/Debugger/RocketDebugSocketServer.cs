﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using RocketUI.Debugger.Models;
using WebSocketSharp;
using WebSocketSharp.Net.WebSockets;
using WebSocketSharp.Server;

namespace RocketUI.Debugger
{
    public class RocketDebugSocketServer : IRocketDebugSocket, IHostedService, IDisposable
    {
        private readonly IServiceProvider _serviceProvider;

        private static JsonSerializerSettings _serializerSettings = new JsonSerializerSettings()
        {
            Culture = CultureInfo.InvariantCulture,
            MaxDepth = 64,
            NullValueHandling = NullValueHandling.Include,
            ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
            ContractResolver = new CamelCasePropertyNamesContractResolver()
        };

        private static JsonSerializerSettings _deserializerSettings = new JsonSerializerSettings()
        {
            Culture = CultureInfo.InvariantCulture,
//            MaxDepth = 128, // no max depth pls3
            NullValueHandling = NullValueHandling.Include,
            ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
            ContractResolver = new CamelCasePropertyNamesContractResolver()
        };

        private GuiManager _guiManager => _serviceProvider.GetRequiredService<GuiManager>();

        private WebSocketServer _webSocket;

        public RocketDebugSocketServer(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
            _webSocket = new WebSocketServer("ws://localhost:2345");
            _webSocket.AddWebSocketService<RocketDebugWebsocketService>("/",
                () => new RocketDebugWebsocketService(this));
        }

        internal object HandleMessage(Message msg)
        {
            switch (msg.Command)
            {
                case "GetRoot":
                {
                    var root = _guiManager.FocusManager.ActiveFocusContext?.FocusedControl?.RootScreen ??
                               _guiManager.Screens.FirstOrDefault();
                    return new SanitizedElementDetail(root);
                }
                    break;

                case "GetChildren":
                {
                    var elementId = Guid.Parse(msg.Arguments[0]);
                    var element   = FindElementById(elementId);
                    return element.ChildElements.Select(x => new SanitizedElementDetail(x));
                }
                    break;

                case "GetProperties":
                {
                    var elementId = Guid.Parse(msg.Arguments[0]);
                    var element   = FindElementById(elementId);
                    return new SanitizedElementDetail(element).Properties;
                }
                    break;

                default:

                    break;
            }

            return null;
        }

        //private void Send(WebSocketContext target, Response response)
        // {
        //     target.WebSocket.Send(JsonSerialize(response));
        // }

        private RocketElement FindElementById(Guid id)
        {
            foreach (var screens in _guiManager.Screens.ToArray())
            {
                if (screens.TryFindDeepestChild(guiElement => guiElement.Id == id, out var element))
                {
                    return element as RocketElement;
                }
            }

            return null;
        }

        internal static string JsonSerialize<T>(T obj)
        {
            return JsonConvert.SerializeObject(obj, _serializerSettings);
        }

        internal static T JsonDeserialize<T>(string data)
        {
            return JsonConvert.DeserializeObject<T>(data, _deserializerSettings);
        }

        public void Dispose()
        {
            ((IDisposable) _webSocket)?.Dispose();
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _webSocket.Start();
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _webSocket.Stop();
            return Task.CompletedTask;
        }
    }

    public class RocketDebugWebsocketService : WebSocketBehavior
    {
        private RocketDebugSocketServer _server;

        public RocketDebugWebsocketService(RocketDebugSocketServer server) : base()
        {
            _server = server;
        }

        protected override void OnMessage(MessageEventArgs e)
        {
            try
            {
                var msg      = RocketDebugSocketServer.JsonDeserialize<Message>(e.Data);
                try
                {
                    var response = _server.HandleMessage(msg);
                    var json = RocketDebugSocketServer.JsonSerialize(new Response()
                    {
                        Id = msg.Id,
                        Data = response
                    });
                    Send(json);
                }
                catch (Exception ex)
                {
                    Send(RocketDebugSocketServer.JsonSerialize(new Response()
                    {
                        Id = msg.Id,
                        Data = ex.Message
                    }));
                    Console.WriteLine("Exception while handling websocket message: {0}", ex.ToString());
                }
                
            }
            catch (Exception ex)
            {
                Console.WriteLine("Exception while parsing websocket message: {0}", ex.ToString());
            }
        }

    }

        class SanitizedElementDetail
        {
            public Guid                                 Id         { get; set; }
            public string                               Name       { get; set; }
            public string                               Type       { get; set; }
            public string                               Display    { get; set; }
            public List<SanitizedElementDetailProperty> Properties { get; set; }
            public List<SanitizedElementDetail>         Children   { get; set; }

            public SanitizedElementDetail()
            {
                
            }

            public SanitizedElementDetail(IGuiElement element)
            {
                Id = element.Id;
                Name = element.Name;
                Type = element.GetType().Name;
                Display = $"<{Type}>";
                Properties = GetProperties(element).ToList();
                Children = GetChildren(element).ToList();
            }

            private static IEnumerable<SanitizedElementDetail> GetChildren(IGuiElement element)
            {
                foreach (var child in element.ChildElements)
                {
                    yield return new SanitizedElementDetail(child);
                }
            }
            private static IEnumerable<SanitizedElementDetailProperty> GetProperties(IGuiElement element)
            {
                element.Properties.Initialize();
                foreach (var prop in element.Properties.ToArray())
                {
                    yield return new SanitizedElementDetailProperty(prop.Key.ToString(), prop.Value);
                }
            }
        }

        class SanitizedElementDetailProperty
        {
            public string Key   { get; set; }
            public object Value { get; set; }
            public object ValueType { get; set; }

            public SanitizedElementDetailProperty()
            {
                
            }

            public SanitizedElementDetailProperty(string key, object value)
            {
                Key = key;
                Value = value;
                ValueType = value?.GetType();
            }
        }
}