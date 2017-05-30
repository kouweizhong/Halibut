using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Halibut.Diagnostics;
using Halibut.ServiceModel;
using Halibut.Transport.Protocol;
using Halibut.Util;
using NSubstitute;
using Xunit;

namespace Halibut.Tests
{
    public class ProtocolFixture
    {
        MessageExchangeProtocol protocol;
        DumpStream stream;

        public ProtocolFixture()
        {
            stream = new DumpStream();
            stream.SetRemoteIdentity(new RemoteIdentity(RemoteIdentityType.Server));
            protocol = new MessageExchangeProtocol(stream);
        }

        [Fact]
        public async Task ShouldExchangeAsClient()
        {
            await protocol.ExchangeAsClient(new RequestMessage()).ConfigureAwait(false);

            AssertOutput(@"
--> MX-CLIENT
<-- MX-SERVER
--> RequestMessage
<-- ResponseMessage");
        }

        [Fact]
        public async Task ShouldExchangeAsServerOfClient()
        {
            stream.SetRemoteIdentity(new RemoteIdentity(RemoteIdentityType.Client));
            stream.NextReadReturns(new RequestMessage());
            stream.SetNumberOfReads(1);

            await protocol.ExchangeAsServer(req => Task.FromResult(ResponseMessage.FromException(req, new Exception("Divide by zero"))), ri => new PendingRequestQueue(new InMemoryConnectionLog("x")), CancellationToken.None).ConfigureAwait(false);

            AssertOutput(@"
<-- MX-CLIENT || MX-SUBSCRIBE subscriptionId
--> MX-SERVER
<-- RequestMessage
--> ResponseMessage
<-- END");
        }

        [Fact]
        public async Task ShouldExchangeAsClientWithPooling()
        {
            // When connections are pooled (kept alive), we send HELLO and expect a PROCEED before each request, that way we can know whether
            // the connection was torn down first or not without attempting to transmit our request
            await protocol.ExchangeAsClient(new RequestMessage()).ConfigureAwait(false);
            await protocol.ExchangeAsClient(new RequestMessage()).ConfigureAwait(false);
            await protocol.ExchangeAsClient(new RequestMessage()).ConfigureAwait(false);

            AssertOutput(@"
--> MX-CLIENT
<-- MX-SERVER
--> RequestMessage
<-- ResponseMessage
--> NEXT
<-- PROCEED
--> RequestMessage
<-- ResponseMessage
--> NEXT
<-- PROCEED
--> RequestMessage
<-- ResponseMessage");
        }

        [Fact]
        public async Task ShouldExchangeAsServerOfClientWithPooling()
        {
            stream.SetRemoteIdentity(new RemoteIdentity(RemoteIdentityType.Client));
            stream.NextReadReturns(new RequestMessage());
            stream.NextReadReturns(new RequestMessage());
            stream.NextReadReturns(new RequestMessage());

            await protocol.ExchangeAsServer(req => Task.FromResult(ResponseMessage.FromException(req, new Exception("Divide by zero"))), ri => new PendingRequestQueue(new InMemoryConnectionLog("x")), CancellationToken.None).ConfigureAwait(false);

            AssertOutput(@"
<-- MX-CLIENT || MX-SUBSCRIBE subscriptionId
--> MX-SERVER
<-- RequestMessage
--> ResponseMessage
<-- NEXT
--> PROCEED
<-- RequestMessage
--> ResponseMessage
<-- NEXT
--> PROCEED
<-- RequestMessage
--> ResponseMessage
<-- END");
        }

        [Fact]
        public async Task ShouldExchangeAsSubscriber()
        {
            stream.NextReadReturns(new RequestMessage());
            stream.NextReadReturns(new RequestMessage());
            stream.NextReadReturns(new RequestMessage());
            
            await protocol.ExchangeAsSubscriber(new Uri("poll://12831"), req => Task.FromResult(ResponseMessage.FromException(req, new Exception("Divide by zero"))), 5).ConfigureAwait(false);
            
            AssertOutput(@"
--> MX-SUBSCRIBE subscriptionId
<-- MX-SERVER
<-- RequestMessage
--> ResponseMessage
--> NEXT
<-- PROCEED
<-- RequestMessage
--> ResponseMessage
--> NEXT
<-- PROCEED
<-- RequestMessage
--> ResponseMessage
--> NEXT
<-- PROCEED
<-- RequestMessage
--> NEXT
<-- PROCEED
<-- RequestMessage
--> NEXT
<-- PROCEED");
        }

        [Fact]
        public async Task ShouldExchangeAsServerOfSubscriber()
        {
            stream.SetRemoteIdentity(new RemoteIdentity(RemoteIdentityType.Subscriber, new Uri("poll://12831")));
            var requestQueue = Substitute.For<IPendingRequestQueue>();
            var queue = new Queue<RequestMessage>();
            queue.Enqueue(new RequestMessage());
            queue.Enqueue(new RequestMessage());
            requestQueue.DequeueAsync().Returns(ci => queue.Count > 0 ? queue.Dequeue() : null);
            stream.SetNumberOfReads(2);

            await protocol.ExchangeAsServer(req => Task.FromResult(ResponseMessage.FromException(req, new Exception("Divide by zero"))), ri => requestQueue, CancellationToken.None).ConfigureAwait(false);

            AssertOutput(@"
<-- MX-CLIENT || MX-SUBSCRIBE subscriptionId
--> MX-SERVER
--> RequestMessage
<-- ResponseMessage
<-- NEXT
--> PROCEED
--> RequestMessage
<-- ResponseMessage
<-- END");
        }

        [Fact]
        public async Task ShouldExchangeAsServerOfSubscriberAsync()
        {
            stream.SetRemoteIdentity(new RemoteIdentity(RemoteIdentityType.Subscriber, new Uri("poll://12831")));
            var requestQueue = Substitute.For<IPendingRequestQueue>();
            var queue = new Queue<RequestMessage>();
            queue.Enqueue(new RequestMessage());
            queue.Enqueue(new RequestMessage());
            requestQueue.DequeueAsync().Returns(ci => queue.Count > 0 ? queue.Dequeue() : null);
            stream.SetNumberOfReads(2);

            await protocol.ExchangeAsServer(req => Task.FromResult(ResponseMessage.FromException(req, new Exception("Divide by zero"))), ri => requestQueue, CancellationToken.None).ConfigureAwait(false);

            AssertOutput(@"
<-- MX-CLIENT || MX-SUBSCRIBE subscriptionId
--> MX-SERVER
--> RequestMessage
<-- ResponseMessage
<-- NEXT
--> PROCEED
--> RequestMessage
<-- ResponseMessage
<-- END");
        }

        [Fact]
        public async Task ShouldExchangeAsSubscriberWithPooling()
        {
            stream.NextReadReturns(new RequestMessage());
            stream.NextReadReturns(new RequestMessage());

            await protocol.ExchangeAsSubscriber(new Uri("poll://12831"), req => Task.FromResult(ResponseMessage.FromException(req, new Exception("Divide by zero"))), 5).ConfigureAwait(false);

            stream.NextReadReturns(new RequestMessage());

            await protocol.ExchangeAsSubscriber(new Uri("poll://12831"), req => Task.FromResult(ResponseMessage.FromException(req, new Exception("Divide by zero"))), 5).ConfigureAwait(false);

            AssertOutput(@"
--> MX-SUBSCRIBE subscriptionId
<-- MX-SERVER
<-- RequestMessage
--> ResponseMessage
--> NEXT
<-- PROCEED
<-- RequestMessage
--> ResponseMessage
--> NEXT
<-- PROCEED
<-- RequestMessage
--> NEXT
<-- PROCEED
<-- RequestMessage
--> NEXT
<-- PROCEED
<-- RequestMessage
--> NEXT
<-- PROCEED
<-- RequestMessage
--> ResponseMessage
--> NEXT
<-- PROCEED
<-- RequestMessage
--> NEXT
<-- PROCEED
<-- RequestMessage
--> NEXT
<-- PROCEED
<-- RequestMessage
--> NEXT
<-- PROCEED
<-- RequestMessage
--> NEXT
<-- PROCEED");
        }

        [Fact]
        public async Task ShouldExchangeAsServerOfSubscriberWithPooling()
        {
            stream.SetRemoteIdentity(new RemoteIdentity(RemoteIdentityType.Subscriber, new Uri("poll://12831")));
            var requestQueue = Substitute.For<IPendingRequestQueue>();
            var queue = new Queue<RequestMessage>();
            requestQueue.DequeueAsync().Returns(ci => queue.Count > 0 ? queue.Dequeue() : null);

            queue.Enqueue(new RequestMessage());
            queue.Enqueue(new RequestMessage());
            stream.SetNumberOfReads(2);

            await protocol.ExchangeAsServer(req => Task.FromResult(ResponseMessage.FromException(req, new Exception("Divide by zero"))), ri => requestQueue, CancellationToken.None).ConfigureAwait(false);

            queue.Enqueue(new RequestMessage());

            stream.SetNumberOfReads(1);

            await protocol.ExchangeAsServer(req => Task.FromResult(ResponseMessage.FromException(req, new Exception("Divide by zero"))), ri => requestQueue, CancellationToken.None).ConfigureAwait(false);

            AssertOutput(@"
<-- MX-CLIENT || MX-SUBSCRIBE subscriptionId
--> MX-SERVER
--> RequestMessage
<-- ResponseMessage
<-- NEXT
--> PROCEED
--> RequestMessage
<-- ResponseMessage
<-- END
<-- MX-CLIENT || MX-SUBSCRIBE subscriptionId
--> MX-SERVER
--> RequestMessage
<-- ResponseMessage
<-- END");
        }


        [Fact]
        public async Task ShouldExchangeAsServerOfSubscriberWithPoolingAsync()
        {
            stream.SetRemoteIdentity(new RemoteIdentity(RemoteIdentityType.Subscriber, new Uri("poll://12831")));
            var requestQueue = Substitute.For<IPendingRequestQueue>();
            var queue = new Queue<RequestMessage>();
            requestQueue.DequeueAsync().Returns(ci => queue.Count > 0 ? queue.Dequeue() : null);

            queue.Enqueue(new RequestMessage());
            queue.Enqueue(new RequestMessage());
            stream.SetNumberOfReads(2);

            await protocol.ExchangeAsServer(req => Task.FromResult(ResponseMessage.FromException(req, new Exception("Divide by zero"))), ri => requestQueue, CancellationToken.None).ConfigureAwait(false);

            queue.Enqueue(new RequestMessage());

            stream.SetNumberOfReads(1);

            await protocol.ExchangeAsServer(req => Task.FromResult(ResponseMessage.FromException(req, new Exception("Divide by zero"))), ri => requestQueue, CancellationToken.None).ConfigureAwait(false);

            AssertOutput(@"
<-- MX-CLIENT || MX-SUBSCRIBE subscriptionId
--> MX-SERVER
--> RequestMessage
<-- ResponseMessage
<-- NEXT
--> PROCEED
--> RequestMessage
<-- ResponseMessage
<-- END
<-- MX-CLIENT || MX-SUBSCRIBE subscriptionId
--> MX-SERVER
--> RequestMessage
<-- ResponseMessage
<-- END");
        }

        void AssertOutput(string expected)
        {
            Trace.WriteLine(stream.ToString());
            stream.ToString().Replace("\r\n", "\n").Trim().Should().Be(expected.Replace("\r\n", "\n").Trim());
        }

        class DumpStream : IMessageExchangeStream
        {
            readonly StringBuilder output = new StringBuilder();
            readonly Queue<object> nextReadQueue = new Queue<object>();
            RemoteIdentity remoteIdentity;
            int numberOfReads = 3;

            public DumpStream()
            {
                Sent = new List<object>();
            }

            public void NextReadReturns(object o)
            {
                nextReadQueue.Enqueue(o);
            }

            public void SetRemoteIdentity(RemoteIdentity identity)
            {
                remoteIdentity = identity;
            }

            public void SetNumberOfReads(int reads)
            {
                numberOfReads = reads;
            }

            public List<object> Sent { get; set; } 

            public Task IdentifyAsClient()
            {
                output.AppendLine("--> MX-CLIENT");
                output.AppendLine("<-- MX-SERVER");

                return TaskEx.CompletedTask;
            }

            public Task SendNext()
            {
                output.AppendLine("--> NEXT");
                return TaskEx.CompletedTask;
            }

            public Task SendProceed()
            {
                output.AppendLine("--> PROCEED");
                return TaskEx.CompletedTask;
            }

            public Task<bool> ExpectNextOrEnd()
            {
                if (--numberOfReads == 0)
                {
                    output.AppendLine("<-- END");
                    return Task.FromResult(false);
                }
                output.AppendLine("<-- NEXT");
                return Task.FromResult(true);
            }

            public Task ExpectProceeed()
            {
                output.AppendLine("<-- PROCEED");
                return TaskEx.CompletedTask;
            }

            public Task IdentifyAsSubscriber(string subscriptionId)
            {
                output.AppendLine("--> MX-SUBSCRIBE subscriptionId");
                output.AppendLine("<-- MX-SERVER");
                return TaskEx.CompletedTask;
            }

            public Task IdentifyAsServer()
            {
                output.AppendLine("--> MX-SERVER");
                return TaskEx.CompletedTask;
            }

            public Task<RemoteIdentity> ReadRemoteIdentity()
            {
                output.AppendLine("<-- MX-CLIENT || MX-SUBSCRIBE subscriptionId");
                return Task.FromResult(remoteIdentity);
            }

            public Task Send<T>(T message)
            {
                output.AppendLine("--> " + typeof(T).Name);
                Sent.Add(message);

                return TaskEx.CompletedTask;
            }

            public Task<T> Receive<T>()
            {
                output.AppendLine("<-- " + typeof(T).Name);     
                return Task.FromResult((T)(nextReadQueue.Count > 0 ? nextReadQueue.Dequeue() : default(T)));
            }

            public override string ToString()
            {
                return output.ToString();
            }
        }
    }
}