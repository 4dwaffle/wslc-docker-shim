using System.Net;
using System.Net.Http.Json;
using System.Buffers.Binary;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.AspNetCore.TestHost;
using Testcontainers.WslcShim.Docker;
using Testcontainers.WslcShim.Http;
using Testcontainers.WslcShim.Ryuk;

namespace Testcontainers.WslcShim.Tests;

public sealed class ShimHttpEndpointTests
{
    [Fact]
    public async Task Full_listener_mutates_ryuk_create_request_before_backend_create()
    {
        var backend = new RecordingDockerBackend();
        using var server = await ShimTestServer.CreateAsync(backend);
        var client = server.GetTestClient();

        var response = await client.PostAsJsonAsync("/containers/create?name=testcontainers-ryuk-session-a",
            new DockerContainerCreateRequest
            {
                Image = "testcontainers/ryuk:0.12.0",
                Env = ["RYUK_CONNECTION_TIMEOUT=30s"],
                HostConfig = new DockerHostConfig
                {
                    Binds = ["/var/run/docker.sock:/var/run/docker.sock:ro"]
                }
            });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = Assert.Single(backend.CreatedContainers);
        Assert.True(created.IsRyuk);
        Assert.Contains("DOCKER_HOST=tcp://172.28.48.1:49152", created.Request.Env);
        Assert.DoesNotContain(created.Request.HostConfig.Binds, bind => bind.Contains("/var/run/docker.sock"));
    }

    [Theory]
    [InlineData("/v1.43/_ping", "OK")]
    [InlineData("/v1.43/version", "wslc-docker-shim")]
    public async Task Routes_accept_docker_api_version_prefix(string path, string expectedContent)
    {
        using var server = await ShimTestServer.CreateAsync(new RecordingDockerBackend());
        var client = server.GetTestClient();

        var body = await (await client.GetAsync(path)).Content.ReadAsStringAsync();

        Assert.Contains(expectedContent, body);
    }

    [Fact]
    public async Task Full_listener_returns_docker_system_info()
    {
        using var server = await ShimTestServer.CreateAsync(new RecordingDockerBackend());
        var client = server.GetTestClient();

        var body = await (await client.GetAsync("/info")).Content.ReadAsStringAsync();

        Assert.Contains("\"osType\":\"linux\"", body);
    }

    [Fact]
    public async Task Ryuk_listener_refuses_full_api_routes()
    {
        using var server = await ShimTestServer.CreateAsync(new RecordingDockerBackend());
        var client = server.GetTestClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/containers/create");
        request.Headers.Add(HeaderListenerClassifier.ListenerHeaderName, "ryuk");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Full_listener_starts_containers()
    {
        var backend = new RecordingDockerBackend();
        using var server = await ShimTestServer.CreateAsync(backend);
        var client = server.GetTestClient();

        var response = await client.PostAsync("/containers/container-1/start", null);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Contains((DockerResourceKind.Container, "container-1"), backend.StartedResources);
    }

    [Fact]
    public async Task Full_listener_inspects_containers()
    {
        var backend = new RecordingDockerBackend();
        backend.InspectJson[(DockerResourceKind.Container, "container-1")] =
            """{"Id":"container-1","Name":"test"}""";
        using var server = await ShimTestServer.CreateAsync(backend);
        var client = server.GetTestClient();

        var body = await (await client.GetAsync("/containers/container-1/json")).Content.ReadAsStringAsync();

        Assert.Contains("container-1", body);
    }

    [Fact]
    public async Task Full_listener_returns_container_logs_as_docker_raw_stream_without_http_chunk_markers()
    {
        var backend = new RecordingDockerBackend
        {
            ContainerLogs = "Started\n"
        };
        using var server = await ShimTestServer.CreateAsync(backend);
        var client = server.GetTestClient();

        var body = await (await client.GetAsync("/containers/container-1/logs?stdout=true&stderr=true")).Content.ReadAsByteArrayAsync();

        Assert.Equal(1, body[0]);
        Assert.Equal(new byte[] { 0, 0, 0 }, body[1..4]);
        Assert.Equal("Started\n".Length, (int)BinaryPrimitives.ReadUInt32BigEndian(body.AsSpan(4, 4)));
        Assert.Equal("Started\n", System.Text.Encoding.UTF8.GetString(body[8..]));
    }

    [Fact]
    public async Task Full_listener_waits_for_container_and_returns_exit_status()
    {
        var backend = new RecordingDockerBackend
        {
            WaitResponse = new DockerWaitContainerResponse(17)
        };
        using var server = await ShimTestServer.CreateAsync(backend);
        var client = server.GetTestClient();

        var response = await client.PostAsync("/containers/container-1/wait", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"StatusCode\":17", body);
        Assert.Equal("container-1", backend.WaitedContainers.Single());
    }

    [Fact]
    public async Task Full_listener_creates_starts_and_inspects_exec_commands()
    {
        var backend = new RecordingDockerBackend
        {
            ExecStartResponse = new DockerExecStartResponse("sqlcmd\n", 0)
        };
        using var server = await ShimTestServer.CreateAsync(backend);
        var client = server.GetTestClient();

        var createResponse = await client.PostAsJsonAsync("/containers/container-1/exec", new DockerExecCreateRequest
        {
            Cmd = ["which", "sqlcmd"],
            Env = ["A=B"]
        });
        var createBody = await createResponse.Content.ReadFromJsonAsync<DockerExecCreateResponse>();
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        Assert.Equal("exec-1", createBody?.Id);

        var startResponse = await client.PostAsJsonAsync("/exec/exec-1/start", new DockerExecStartRequest());
        var startBody = await startResponse.Content.ReadAsByteArrayAsync();
        var inspectBody = await (await client.GetAsync("/exec/exec-1/json")).Content.ReadAsStringAsync();

        Assert.Equal("sqlcmd\n", System.Text.Encoding.UTF8.GetString(startBody[8..]));
        Assert.Contains("\"ExitCode\":0", inspectBody);
        var createdExec = backend.CreatedExecs.Single();
        Assert.Equal("container-1", createdExec.ContainerId);
        Assert.Equal(["which", "sqlcmd"], createdExec.Command);
        Assert.Equal("exec-1", backend.StartedExecs.Single());
    }

    [Fact]
    public async Task Full_listener_inspects_images_with_repository_slashes()
    {
        var backend = new RecordingDockerBackend();
        backend.InspectJson[(DockerResourceKind.Image, "testcontainers/ryuk:0.12.0")] =
            """{"Id":"sha256:ryuk","RepoTags":["testcontainers/ryuk:0.12.0"]}""";
        using var server = await ShimTestServer.CreateAsync(backend);
        var client = server.GetTestClient();

        var body = await (await client.GetAsync("/images/testcontainers/ryuk:0.12.0/json")).Content.ReadAsStringAsync();

        Assert.Contains("sha256:ryuk", body);
    }

    [Fact]
    public async Task Ryuk_listener_refuses_container_inspect()
    {
        var backend = new RecordingDockerBackend();
        using var server = await ShimTestServer.CreateAsync(backend);
        var client = server.GetTestClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/containers/container-1/json");
        request.Headers.Add(HeaderListenerClassifier.ListenerHeaderName, "ryuk");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Ryuk_listener_refuses_container_start()
    {
        var backend = new RecordingDockerBackend();
        using var server = await ShimTestServer.CreateAsync(backend);
        var client = server.GetTestClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/containers/container-1/start");
        request.Headers.Add(HeaderListenerClassifier.ListenerHeaderName, "ryuk");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(backend.StartedResources);
    }

    [Fact]
    public async Task Full_listener_pulls_images()
    {
        var backend = new RecordingDockerBackend();
        using var server = await ShimTestServer.CreateAsync(backend);
        var client = server.GetTestClient();

        var response = await client.PostAsync("/images/create?fromImage=redis&tag=7", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(["redis:7"], backend.PulledImages);
    }

    [Theory]
    [InlineData("/networks/create", DockerResourceKind.Network, "network-a")]
    [InlineData("/volumes/create", DockerResourceKind.Volume, "volume-a")]
    public async Task Full_listener_creates_named_resources(
        string path,
        DockerResourceKind kind,
        string name)
    {
        var backend = new RecordingDockerBackend();
        using var server = await ShimTestServer.CreateAsync(backend);
        var client = server.GetTestClient();

        var response = await client.PostAsJsonAsync(path, new DockerResourceCreateRequest
        {
            Name = name,
            Labels = new Dictionary<string, string>
            {
                ["org.testcontainers"] = "true"
            }
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Contains(backend.CreatedResources, created =>
            created.Kind == kind &&
            created.Request.Name == name &&
            created.Request.Labels["org.testcontainers"] == "true");
    }

    [Fact]
    public async Task Ryuk_listener_refuses_delete_when_resource_does_not_match_session_filter()
    {
        var backend = new RecordingDockerBackend();
        backend.Resources[DockerResourceKind.Container]["container-1"] = new DockerResourceSnapshot(
            "container-1",
            new Dictionary<string, string>
            {
                ["org.testcontainers"] = "true",
                ["org.testcontainers.session-id"] = "session-b"
            });
        using var server = await ShimTestServer.CreateAsync(backend);
        var client = server.GetTestClient();
        using var request = new HttpRequestMessage(
            HttpMethod.Delete,
            "/containers/container-1?filters=%7B%22label%22%3A%5B%22org.testcontainers%3Dtrue%22%2C%22org.testcontainers.session-id%3Dsession-a%22%5D%7D");
        request.Headers.Add(HeaderListenerClassifier.ListenerHeaderName, "ryuk");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(backend.DeletedContainers);
    }

    [Fact]
    public async Task Ryuk_listener_deletes_resources_matching_testcontainers_session_filter()
    {
        var backend = new RecordingDockerBackend();
        backend.Resources[DockerResourceKind.Container]["container-1"] = new DockerResourceSnapshot(
            "container-1",
            new Dictionary<string, string>
            {
                ["org.testcontainers"] = "true",
                ["org.testcontainers.session-id"] = "session-a"
            });
        using var server = await ShimTestServer.CreateAsync(backend);
        var client = server.GetTestClient();
        using var request = new HttpRequestMessage(
            HttpMethod.Delete,
            "/containers/container-1?filters=%7B%22label%22%3A%5B%22org.testcontainers%3Dtrue%22%2C%22org.testcontainers.session-id%3Dsession-a%22%5D%7D");
        request.Headers.Add(HeaderListenerClassifier.ListenerHeaderName, "ryuk");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(["container-1"], backend.DeletedContainers);
    }

    [Theory]
    [InlineData("/networks/network-1", DockerResourceKind.Network, "network-1")]
    [InlineData("/volumes/volume-1", DockerResourceKind.Volume, "volume-1")]
    [InlineData("/images/image-1", DockerResourceKind.Image, "image-1")]
    public async Task Ryuk_listener_deletes_matching_non_container_resources(
        string path,
        DockerResourceKind resourceKind,
        string resourceId)
    {
        var backend = new RecordingDockerBackend();
        backend.Resources[resourceKind][resourceId] = new DockerResourceSnapshot(
            resourceId,
            new Dictionary<string, string>
            {
                ["org.testcontainers"] = "true",
                ["org.testcontainers.session-id"] = "session-a"
            });
        using var server = await ShimTestServer.CreateAsync(backend);
        var client = server.GetTestClient();
        using var request = new HttpRequestMessage(
            HttpMethod.Delete,
            $"{path}?filters=%7B%22label%22%3A%5B%22org.testcontainers%3Dtrue%22%2C%22org.testcontainers.session-id%3Dsession-a%22%5D%7D");
        request.Headers.Add(HeaderListenerClassifier.ListenerHeaderName, "ryuk");

        var response = await client.SendAsync(request);

        Assert.True(response.StatusCode is HttpStatusCode.NoContent or HttpStatusCode.OK);
        Assert.Contains((resourceKind, resourceId), backend.DeletedResources);
    }

    [Theory]
    [InlineData("/containers/json")]
    [InlineData("/networks")]
    [InlineData("/volumes")]
    [InlineData("/images/json")]
    public async Task Ryuk_listener_rejects_list_requests_without_testcontainers_session_filter(string path)
    {
        var backend = new RecordingDockerBackend();
        backend.Resources[DockerResourceKind.Container]["container-1"] = new DockerResourceSnapshot(
            "container-1",
            new Dictionary<string, string>
            {
                ["org.testcontainers"] = "true",
                ["org.testcontainers.session-id"] = "session-a"
            });
        using var server = await ShimTestServer.CreateAsync(backend);
        var client = server.GetTestClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add(HeaderListenerClassifier.ListenerHeaderName, "ryuk");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("/containers/json", DockerResourceKind.Container, "container-1")]
    [InlineData("/networks", DockerResourceKind.Network, "network-1")]
    [InlineData("/volumes", DockerResourceKind.Volume, "volume-1")]
    [InlineData("/images/json", DockerResourceKind.Image, "image-1")]
    public async Task Ryuk_listener_lists_resources_with_docker_label_filters(
        string path,
        DockerResourceKind resourceKind,
        string resourceId)
    {
        var backend = new RecordingDockerBackend();
        backend.Resources[resourceKind][resourceId] = new DockerResourceSnapshot(
            resourceId,
            new Dictionary<string, string>
            {
                ["org.testcontainers"] = "true",
                ["org.testcontainers.session-id"] = "session-a"
            });
        backend.Resources[resourceKind]["other"] = new DockerResourceSnapshot(
            "other",
            new Dictionary<string, string>
            {
                ["org.testcontainers"] = "true",
                ["org.testcontainers.session-id"] = "session-b"
            });
        using var server = await ShimTestServer.CreateAsync(backend);
        var client = server.GetTestClient();
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"{path}?filters=%7B%22label%22%3A%5B%22org.testcontainers%3Dtrue%22%2C%22org.testcontainers.session-id%3Dsession-a%22%5D%7D");
        request.Headers.Add(HeaderListenerClassifier.ListenerHeaderName, "ryuk");

        var body = await (await client.SendAsync(request)).Content.ReadAsStringAsync();

        Assert.Contains(resourceId, body);
        Assert.DoesNotContain("other", body);
    }

    private sealed class RecordingDockerBackend : IWslcDockerBackend
    {
        public RecordingDockerBackend()
        {
            foreach (var resourceKind in Enum.GetValues<DockerResourceKind>())
            {
                Resources[resourceKind] = new Dictionary<string, DockerResourceSnapshot>(StringComparer.Ordinal);
            }
        }

        public Dictionary<DockerResourceKind, Dictionary<string, DockerResourceSnapshot>> Resources { get; } = [];

        public List<(DockerContainerCreateRequest Request, bool IsRyuk)> CreatedContainers { get; } = [];

        public List<string> DeletedContainers { get; } = [];

        public List<(DockerResourceKind Kind, string Id)> DeletedResources { get; } = [];

        public List<(DockerResourceKind Kind, string Id)> StartedResources { get; } = [];

        public List<string> WaitedContainers { get; } = [];

        public List<(string ContainerId, string[] Command)> CreatedExecs { get; } = [];

        public List<string> StartedExecs { get; } = [];

        public List<string> PulledImages { get; } = [];

        public List<(DockerResourceKind Kind, DockerResourceCreateRequest Request)> CreatedResources { get; } = [];

        public Dictionary<(DockerResourceKind Kind, string Id), string> InspectJson { get; } = [];

        public DockerWaitContainerResponse? WaitResponse { get; init; }

        public DockerExecStartResponse? ExecStartResponse { get; init; }

        public string ContainerLogs { get; init; } = string.Empty;

        public Task<DockerCreateContainerResponse> CreateContainerAsync(
            DockerContainerCreateRequest request,
            bool isRyuk,
            CancellationToken cancellationToken)
        {
            CreatedContainers.Add((request, isRyuk));
            return Task.FromResult(new DockerCreateContainerResponse("container-created"));
        }

        public Task<IReadOnlyList<DockerResourceSnapshot>> ListResourcesAsync(
            DockerResourceKind kind,
            DockerLabelFilters filters,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<DockerResourceSnapshot>>(
                Resources[kind].Values.Where(resource => filters.Matches(resource.Labels)).ToArray());
        }

        public Task<DockerResourceSnapshot?> InspectResourceAsync(
            DockerResourceKind kind,
            string id,
            CancellationToken cancellationToken)
        {
            Resources[kind].TryGetValue(id, out var snapshot);
            return Task.FromResult(snapshot);
        }

        public Task DeleteResourceAsync(DockerResourceKind kind, string id, CancellationToken cancellationToken)
        {
            DeletedResources.Add((kind, id));
            if (kind == DockerResourceKind.Container)
            {
                DeletedContainers.Add(id);
            }

            return Task.CompletedTask;
        }

        public Task<string?> InspectResourceJsonAsync(
            DockerResourceKind kind,
            string id,
            CancellationToken cancellationToken)
        {
            InspectJson.TryGetValue((kind, id), out var json);
            return Task.FromResult(json);
        }

        public Task StartContainerAsync(string id, CancellationToken cancellationToken)
        {
            StartedResources.Add((DockerResourceKind.Container, id));
            return Task.CompletedTask;
        }

        public Task StopContainerAsync(string id, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task<string> GetContainerLogsAsync(string id, DockerLogRequest request, CancellationToken cancellationToken)
        {
            return Task.FromResult(ContainerLogs);
        }

        public Task<DockerWaitContainerResponse?> WaitContainerAsync(string id, CancellationToken cancellationToken)
        {
            WaitedContainers.Add(id);
            return Task.FromResult(WaitResponse);
        }

        public Task<DockerExecCreateResponse> CreateContainerExecAsync(
            string containerId,
            DockerExecCreateRequest request,
            CancellationToken cancellationToken)
        {
            CreatedExecs.Add((containerId, request.Cmd.ToArray()));
            return Task.FromResult(new DockerExecCreateResponse("exec-1"));
        }

        public Task<DockerExecStartResponse?> StartExecAsync(
            string id,
            DockerExecStartRequest request,
            CancellationToken cancellationToken)
        {
            StartedExecs.Add(id);
            return Task.FromResult(ExecStartResponse);
        }

        public Task<DockerExecInspectResponse?> InspectExecAsync(string id, CancellationToken cancellationToken)
        {
            return Task.FromResult<DockerExecInspectResponse?>(new DockerExecInspectResponse(id, false, ExecStartResponse?.ExitCode ?? 0));
        }

        public Task PullImageAsync(string image, CancellationToken cancellationToken)
        {
            PulledImages.Add(image);
            return Task.CompletedTask;
        }

        public Task<DockerResourceSnapshot> CreateResourceAsync(
            DockerResourceKind kind,
            DockerResourceCreateRequest request,
            CancellationToken cancellationToken)
        {
            CreatedResources.Add((kind, request));
            var snapshot = new DockerResourceSnapshot(request.Name ?? "generated", request.Labels, request.Name);
            Resources[kind][snapshot.Id] = snapshot;
            return Task.FromResult(snapshot);
        }
    }

    private static class ShimTestServer
    {
        public static Task<IHost> CreateAsync(RecordingDockerBackend backend)
        {
            var options = new ShimRuntimeOptions
            {
                FullApiPort = 23755,
                RyukEndpoint = new RyukListenerEndpoint("172.28.48.1", 49152)
            };

            return new HostBuilder()
                .ConfigureWebHost(builder =>
                {
                    builder.UseTestServer()
                        .ConfigureServices(services =>
                        {
                            ShimApplication.ConfigureServices(services, backend, options, new HeaderListenerClassifier());
                        })
                        .Configure(app =>
                        {
                            app.UseDockerApiVersionPrefix();
                            app.UseRouting();
                            app.UseEndpoints(ShimApplication.MapRoutes);
                        });
                })
                .StartAsync();
        }
    }
}
