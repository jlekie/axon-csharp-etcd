using System;
using System.Linq;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using System.Security.Cryptography;

using dotnet_etcd;

namespace Axon.Etcd
{
    public interface IEtcdAnnouncer : IAnnouncer
    {
    }
    public class EtcdAnnouncer : AAnnouncer, IEtcdAnnouncer
    {
        private readonly EtcdClient client;

        public EtcdAnnouncer(string identifier, string hostname, int port)
            : base(identifier)
        {
            this.client = new EtcdClient(hostname, port);
        }

        public async override Task Register(IEncodableEndpoint endpoint)
        {
            var encodedEndpoint = endpoint.Encode();

            string endpointHash;
            using (var sha1 = new SHA1Managed())
                endpointHash = BitConverter.ToString(sha1.ComputeHash(encodedEndpoint)).Replace("-", string.Empty).ToLower();

            var keyLease = await this.client.LeaseGrantAsync(new Etcdserverpb.LeaseGrantRequest() { TTL = 60 });
            await this.client.PutAsync($"axon/{this.Identifier}/{endpointHash}", Encoding.UTF8.GetString(encodedEndpoint), keyLease.ID);
        }
    }

    public interface IEtcdDiscoverer<TEndpoint> : IDiscoverer<TEndpoint> where TEndpoint : IEncodableEndpoint
    {
    }
    public class EtcdDiscoverer<TEndpoint> : ADiscoverer<TEndpoint>, IEtcdDiscoverer<TEndpoint> where TEndpoint: IEncodableEndpoint
    {
        private readonly EtcdClient client;

        private readonly List<string> blacklistedEndpoints;

        public EtcdDiscoverer(string identifier, IEndpointDecoder<TEndpoint> endpointDecoder, string hostname, int port)
            : base(identifier, endpointDecoder)
        {
            this.client = new EtcdClient(hostname, port);
            this.blacklistedEndpoints = new List<string>();
        }

        public async override Task<TEndpoint> Discover(int timeout = 0)
        {
            var endpoints = await this.DiscoverAll(timeout);

            var startTime = DateTime.UtcNow;
            while (endpoints.Length <= 0)
            {
                if ((DateTime.UtcNow - startTime).TotalMilliseconds > timeout)
                    throw new Exception($"Discovery timeout ({this.Identifier})");

                await Task.Delay(100);
                endpoints = await this.DiscoverAll(timeout);
            }

            var rand = new Random();
            return endpoints[rand.Next(0, endpoints.Length - 1)];
        }
        public async override Task<TEndpoint[]> DiscoverAll(int timeout = 0)
        {
            var keys = await this.client.GetRangeAsync($"axon/{this.Identifier}");

            var endpoints = new List<TEndpoint>();
            foreach (var childKey in keys)
            {
                var encodedEndpoint = Encoding.UTF8.GetBytes(childKey.Value);

                string endpointHash;
                using (var sha1 = new SHA1Managed())
                    endpointHash = BitConverter.ToString(sha1.ComputeHash(encodedEndpoint)).Replace("-", string.Empty).ToLower();

                if (!this.blacklistedEndpoints.Contains(endpointHash))
                    endpoints.Add(this.EndpointDecoder.Decode(Encoding.UTF8.GetBytes(childKey.Value)));
            }

            //var encodedEndpoints = keys.Select(childKey =>
            //    this.EndpointDecoder.Decode(Encoding.UTF8.GetBytes(childKey.Value)));

            return endpoints.ToArray();
        }
        public override Task Blacklist(TEndpoint endpoint)
        {
            Console.WriteLine("BLACKLISTING " + Encoding.UTF8.GetString(endpoint.Encode()));

            //var encodedEndpoint = endpoint.Encode();

            //string endpointHash;
            //using (var sha1 = new SHA1Managed())
            //    endpointHash = BitConverter.ToString(sha1.ComputeHash(encodedEndpoint)).Replace("-", string.Empty).ToLower();

            //this.blacklistedEndpoints.Add(endpointHash);

            return Task.FromResult(true);
        }
    }
}
