// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace Microsoft.ServiceFabric.Actors.Migration
{
    using System;
    using System.Fabric;
    using System.Fabric.Description;
    using System.Globalization;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.AspNetCore.Hosting;
    using Microsoft.ServiceFabric.Services.Communication.Runtime;

    /// <summary>
    /// Base class for creating AspNetCore based communication listener for Service Fabric stateless or stateful service.
    /// </summary>
    internal abstract class AspNetCoreCommunicationListener : ICommunicationListener
    {
        private readonly ServiceContext serviceContext;
        private readonly ICommunicationListener internalListener;
        private string urlSuffix = null;
        private bool configuredToUseUniqueServiceUrl = false;

        /// <summary>
        /// Initializes a new instance of the <see cref="AspNetCoreCommunicationListener"/> class.
        /// </summary>
        /// <param name="serviceContext">The context of the service for which this communication listener is being constructed.</param>
        /// <param name="build">Delegate to build Microsoft.AspNetCore.Hosting.IWebHost, endpoint url generated by the listener is given as input to this delegate.
        /// This gives the flexibility to change the url before creating Microsoft.AspNetCore.Hosting.IWebHost if needed.</param>
        public AspNetCoreCommunicationListener(ServiceContext serviceContext, Func<string, AspNetCoreCommunicationListener, IWebHost> build)
        {
            if (serviceContext == null)
            {
                throw new ArgumentNullException("serviceContext");
            }

            if (build == null)
            {
                throw new ArgumentNullException("build");
            }

            this.serviceContext = serviceContext;
            this.internalListener = new WebHostCommunicationListener(build, this);
            this.urlSuffix = string.Empty;
        }

        /// <summary>
        /// Gets the context of the service for which this communication listener is being constructed.
        /// </summary>
        public ServiceContext ServiceContext
        {
            get { return this.serviceContext; }
        }

        /// <summary>
        /// Gets the url suffix to be used based on <see cref="ServiceFabricIntegrationOptions"/> specified in
        /// <see cref="WebHostBuilderServiceFabricExtension.UseServiceFabricIntegration"/>.
        /// </summary>
        public string UrlSuffix
        {
            get
            {
                return this.urlSuffix;
            }
        }

        /// <summary>
        /// This method causes the communication listener to close. Close is a terminal state and
        /// this method causes the transition to close ungracefully. Any outstanding operations
        /// (including close) should be canceled when this method is called.
        /// </summary>
        public virtual void Abort()
        {
            this.internalListener.Abort();
        }

        /// <summary>
        /// This method causes the communication listener to close. Close is a terminal state and
        /// this method allows the communication listener to transition to this state in a graceful manner.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>
        /// A <see cref="System.Threading.Tasks.Task">Task</see> that represents outstanding operation.
        /// </returns>
        public virtual Task CloseAsync(CancellationToken cancellationToken)
        {
            return this.internalListener.CloseAsync(cancellationToken);
        }

        /// <summary>
        /// This method causes the communication listener to be opened. Once the Open
        /// completes, the communication listener becomes usable - accepts and sends messages.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>
        /// A <see cref="System.Threading.Tasks.Task">Task</see> that represents outstanding operation. The result of the Task is
        /// is endpoint string on which IWebHost/IHost is listening.
        /// </returns>
        public virtual Task<string> OpenAsync(CancellationToken cancellationToken)
        {
            return this.internalListener.OpenAsync(cancellationToken);
        }

        /// <summary>
        /// Configures the listener to use UniqueServiceUrl by appending a urlSuffix PartitionId and ReplicaId.
        /// It helps in scenarios when ServiceA listening on a node on port X moves and another Service takes its place on the same node and starts using the same port X,
        /// The UniqueServiceUrl in conjunction with middleware rejects requests meant for serviceA arriving at ServiceB.
        /// Example:
        /// Service A is dynamically assigned port 30000 on node with IP 10.0.0.1, it listens on http://+:30000/ and reports to Naming service http://10.0.0.1:30000/serviceName-A/partitionId-A/replicaId-A
        /// Client resolves URL from NS: http://10.0.0.1:30000/serviceName-A/partitionId-A/replicaId-A and sends a request, Service A compares URL path segments to its own service name, partition ID, replica ID, finds they are equal, serves request.
        /// Now Service A moves to a different node and Service B comes up at the node with IP 10.0.0.1 and is dynamically assigned port 30000.
        /// Service B listens on: http://+:30000/ and reports to NS http://10.0.0.1:30000/serviceName-B/partitionId-B/replicaId-B, Client for Service a sends request to http://10.0.0.1:30000/serviceName-A/partitionId-A/replicaId-A
        /// Service B compares URL path segments to its own service name, partition ID, replica ID, finds they do not match, ends the request and responds with HTTP 410. Client receives 410 and re-resolves for service A.
        /// </summary>
        internal void ConfigureToUseUniqueServiceUrl()
        {
            if (!this.configuredToUseUniqueServiceUrl)
            {
                this.urlSuffix = string.Format(CultureInfo.InvariantCulture, "/{0}/{1}", this.serviceContext.PartitionId, this.serviceContext.ReplicaOrInstanceId);

                if (this.ServiceContext is StatefulServiceContext)
                {
                    // For stateful service, also append a Guid, Guid makes the url unique in scenarios for stateful services when Listener is
                    // created to support read on secondary and change role happens from Primary->Secondary for the replica.
                    this.urlSuffix += "/" + Guid.NewGuid();
                }

                this.configuredToUseUniqueServiceUrl = true;
            }
        }

        /// <summary>
        /// Gets url for this listener to be used with Web Server.
        /// </summary>
        /// <returns>url for this listener to be used with Web Server.</returns>
        protected internal abstract string GetListenerUrl();

        /// <summary>
        /// Retrieves the endpoint resource with a given name from the service manifest.
        /// </summary>
        /// <param name="endpointName">The name of the endpoint.</param>
        /// <returns>The endpoint resource with the specified name.</returns>
        protected EndpointResourceDescription GetEndpointResourceDescription(string endpointName)
        {
            if (endpointName == null)
            {
                throw new ArgumentNullException("endpointName");
            }

            if (!this.serviceContext.CodePackageActivationContext.GetEndpoints().Contains(endpointName))
            {
                throw new InvalidOperationException(string.Format("{0} not found in Service Manifest.", endpointName));
            }

            return this.serviceContext.CodePackageActivationContext.GetEndpoint(endpointName);
        }
    }
}
