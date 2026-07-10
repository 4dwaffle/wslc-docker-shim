using System.Net;
﻿using System.Buffers.Binary;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.Routing;
using Testcontainers.WslcShim.Docker;
using Testcontainers.WslcShim.Docker.Enums;
using Testcontainers.WslcShim.Docker.Models;
using Testcontainers.WslcShim.Http;
using Testcontainers.WslcShim.Http.Models;
using Testcontainers.WslcShim.Ryuk;
using Testcontainers.WslcShim.Ryuk.Models;

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

    [Fact]
    public async Task Full_listener_preserves_supported_create_settings_for_backend_translation()
    {
        var backend = new RecordingDockerBackend();
        using var server = await ShimTestServer.CreateAsync(backend);
        var client = server.GetTestClient();

        var response = await client.PostAsJsonAsync("/containers/create?name=worker",
            new DockerContainerCreateRequest
            {
                Image = "alpine:3.20",
                Hostname = "worker-host",
                User = "1000:1000",
                WorkingDir = "/workspace",
                Entrypoint = ["/bin/sh", "-c"],
                Tty = true,
                HostConfig = new DockerHostConfig
                {
                    AutoRemove = true,
                    NetworkMode = "test-network",
                    Memory = 256L * 1024 * 1024,
                    Tmpfs = new Dictionary<string, string> { ["/tmp"] = "rw" }
                },
                NetworkingConfig = new DockerNetworkingConfig
                {
                    EndpointsConfig = new Dictionary<string, DockerEndpointSettings>
                    {
                        ["test-network"] = new() { Aliases = ["worker-alias"] }
                    }
                }
            });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = Assert.Single(backend.CreatedContainers).Request;
        Assert.Equal("worker", created.Name);
        Assert.Equal("worker-host", created.Hostname);
        Assert.Equal("1000:1000", created.User);
        Assert.Equal("/workspace", created.WorkingDir);
        Assert.Equal(["/bin/sh", "-c"], created.Entrypoint);
        Assert.True(created.Tty);
        Assert.True(created.HostConfig.AutoRemove);
        Assert.Equal("test-network", created.HostConfig.NetworkMode);
        Assert.Equal(256L * 1024 * 1024, created.HostConfig.Memory);
        Assert.Equal("rw", created.HostConfig.Tmpfs["/tmp"]);
        Assert.Equal(["worker-alias"], created.NetworkingConfig.EndpointsConfig["test-network"].Aliases);
    }

    [Fact]
    public async Task Full_listener_rejects_unsupported_create_settings_instead_of_dropping_them()
    {
        var backend = new RecordingDockerBackend();
        using var server = await ShimTestServer.CreateAsync(backend);
        var client = server.GetTestClient();
        using var content = new StringContent(
            """
            {
              "Image": "alpine:3.20",
              "HostConfig": {
                "Privileged": true
              }
            }
            """,
            System.Text.Encoding.UTF8,
            "application/json");

        var response = await client.PostAsync("/containers/create", content);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("HostConfig.Privileged", body);
        Assert.Empty(backend.CreatedContainers);
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
    public async Task Registers_expected_unversioned_and_versioned_route_catalog_without_duplicates()
    {
        using var server = await ShimTestServer.CreateAsync(new RecordingDockerBackend());
        var dataSource = server.Services.GetRequiredService<EndpointDataSource>();
        var unversionedRoutes = new (string Method, string Pattern)[]
        {
            ("GET", "/_ping"),
            ("GET", "/version"),
            ("GET", "/info"),
            ("POST", "/containers/create"),
            ("POST", "/containers/{id}/start"),
            ("POST", "/containers/{id}/stop"),
            ("POST", "/containers/{id}/wait"),
            ("POST", "/containers/{id}/exec"),
            ("GET", "/containers/{id}/logs"),
            ("GET", "/containers/{id}/json"),
            ("GET", "/containers/json"),
            ("DELETE", "/containers/{id}"),
            ("POST", "/exec/{id}/start"),
            ("GET", "/exec/{id}/json"),
            ("GET", "/networks"),
            ("POST", "/networks/create"),
            ("GET", "/networks/{id}"),
            ("DELETE", "/networks/{id}"),
            ("GET", "/volumes"),
            ("POST", "/volumes/create"),
            ("GET", "/volumes/{id}"),
            ("DELETE", "/volumes/{id}"),
            ("GET", "/images/json"),
            ("POST", "/images/create"),
            ("GET", "/images/{**id}"),
            ("DELETE", "/images/{**id}")
        };
        var expectedRoutes = unversionedRoutes
            .Concat(unversionedRoutes.Select(route =>
                (route.Method, Pattern: "/{dockerApiVersion}" + route.Pattern)))
            .OrderBy(route => route.Pattern, StringComparer.Ordinal)
            .ThenBy(route => route.Method, StringComparer.Ordinal)
            .ToArray();
        var actualRoutes = dataSource.Endpoints
            .OfType<RouteEndpoint>()
            .SelectMany(endpoint =>
                endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.Select(method =>
                    (Method: method, Pattern: endpoint.RoutePattern.RawText ?? string.Empty)) ?? [])
            .OrderBy(route => route.Pattern, StringComparer.Ordinal)
            .ThenBy(route => route.Method, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(52, actualRoutes.Length);
        Assert.Equal(actualRoutes.Length, actualRoutes.Distinct().Count());
        Assert.Equal(expectedRoutes, actualRoutes);
    }

    [Fact]
    public async Task Full_listener_returns_docker_system_info()
    {
        using var server = await ShimTestServer.CreateAsync(new RecordingDockerBackend());
        var client = server.GetTestClient();

        var body = await (await client.GetAsync("/info")).Content.ReadAsStringAsync();

        Assert.Contains("\"OSType\":\"linux\"", body);
        Assert.DoesNotContain("\"osType\"", body);
    }

    [Fact]
    public async Task Full_listener_returns_docker_version_with_wire_property_names()
    {
        using var server = await ShimTestServer.CreateAsync(new RecordingDockerBackend());
        var client = server.GetTestClient();

        var body = await (await client.GetAsync("/version")).Content.ReadAsStringAsync();

        Assert.Contains("\"ApiVersion\":\"1.43\"", body);
        Assert.Contains("\"MinAPIVersion\":\"1.24\"", body);
        Assert.DoesNotContain("\"apiVersion\"", body);
    }

    [Fact]
    public async Task Ryuk_listener_does_not_expose_docker_system_info()
    {
        using var server = await ShimTestServer.CreateAsync(new RecordingDockerBackend());
        var client = server.GetTestClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/info");
        request.Headers.Add(HeaderListenerClassifier.ListenerHeaderName, "ryuk");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
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
    public async Task Full_listener_preserves_docker_wire_casing_in_resource_list_responses()
    {
        var backend = new RecordingDockerBackend();
        var labels = new Dictionary<string, string> { ["org.testcontainers"] = "true" };
        var createdAt = DateTimeOffset.FromUnixTimeSeconds(1783634303);
        backend.Resources[DockerResourceKind.Container]["container-1"] =
            new DockerResourceSnapshot("container-1", labels, "container-1", createdAt);
        backend.Resources[DockerResourceKind.Image]["image-1"] =
            new DockerResourceSnapshot("image-1", labels, "repository:tag", createdAt);
        backend.Resources[DockerResourceKind.Network]["network-1"] =
            new DockerResourceSnapshot("network-1", labels, "network-1", createdAt);
        backend.Resources[DockerResourceKind.Volume]["volume-1"] =
            new DockerResourceSnapshot("volume-1", labels, "volume-1", createdAt);
        using var server = await ShimTestServer.CreateAsync(backend);
        var client = server.GetTestClient();

        using var containers = JsonDocument.Parse(await (await client.GetAsync("/containers/json")).Content.ReadAsStringAsync());
        using var images = JsonDocument.Parse(await (await client.GetAsync("/images/json")).Content.ReadAsStringAsync());
        using var networks = JsonDocument.Parse(await (await client.GetAsync("/networks")).Content.ReadAsStringAsync());
        using var volumes = JsonDocument.Parse(await (await client.GetAsync("/volumes")).Content.ReadAsStringAsync());

        AssertProperties(containers.RootElement[0], "Id", "Names", "Created", "Labels");
        AssertProperties(images.RootElement[0], "Id", "RepoTags", "Created", "Labels");
        AssertProperties(networks.RootElement[0], "Id", "Name", "Created", "Labels");
        AssertProperties(volumes.RootElement, "Volumes", "Warnings");
        AssertProperties(volumes.RootElement.GetProperty("Volumes")[0], "Name", "CreatedAt", "Labels");
    }

    [Fact]
    public async Task Full_listener_preserves_docker_wire_casing_in_resource_create_responses()
    {
        using var server = await ShimTestServer.CreateAsync(new RecordingDockerBackend());
        var client = server.GetTestClient();

        using var networkResponse = await client.PostAsJsonAsync(
            "/networks/create",
            new DockerResourceCreateRequest { Name = "network-a" });
        using var volumeResponse = await client.PostAsJsonAsync(
            "/volumes/create",
            new DockerResourceCreateRequest { Name = "volume-a" });
        using var network = JsonDocument.Parse(await networkResponse.Content.ReadAsStringAsync());
        using var volume = JsonDocument.Parse(await volumeResponse.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.Created, networkResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Created, volumeResponse.StatusCode);
        AssertProperties(network.RootElement, "Id", "Warning");
        AssertProperties(volume.RootElement, "Name");
    }

    private const string ReaperSessionA = "11111111-1111-1111-1111-111111111111";
    private const string ReaperSessionB = "22222222-2222-2222-2222-222222222222";

    [Fact]
    public async Task Ryuk_listener_never_authorizes_delete_from_query_filters()
    {
        var backend = new RecordingDockerBackend();
        backend.Resources[DockerResourceKind.Container]["container-1"] = SessionResource(
            "container-1",
            ReaperSessionA);
        using var server = await ShimTestServer.CreateAsync(backend);
        var client = server.GetTestClient();
        using var request = RyukRequest(
            HttpMethod.Delete,
            $"/containers/container-1?filters={ModernSessionFilters(ReaperSessionA)}");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(backend.DeletedContainers);
    }

    [Theory]
    [InlineData("/containers/json", "/containers/container-1", DockerResourceKind.Container, "container-1", "\"Created\":1783634303")]
    [InlineData("/networks", "/networks/network-1", DockerResourceKind.Network, "network-1", "\"Created\":\"2026-07-09T21:58:23.0000000Z\"")]
    [InlineData("/volumes", "/volumes/volume-1", DockerResourceKind.Volume, "volume-1", "\"CreatedAt\":\"2026-07-09T21:58:23.0000000Z\"")]
    [InlineData("/images/json", "/images/image-1", DockerResourceKind.Image, "image-1", "\"Created\":1783634303")]
    public async Task Ryuk_protocol_lists_with_modern_session_filter_then_deletes_by_id_without_filters(
        string listPath,
        string deletePath,
        DockerResourceKind resourceKind,
        string resourceId,
        string expectedTimestamp)
    {
        var backend = new RecordingDockerBackend();
        backend.Resources[resourceKind][resourceId] = SessionResource(resourceId, ReaperSessionA);
        backend.Resources[resourceKind]["other-session"] = SessionResource("other-session", ReaperSessionB);
        backend.Resources[resourceKind]["unlabelled"] = new DockerResourceSnapshot(
            "unlabelled",
            new Dictionary<string, string>(),
            CreatedAt: DateTimeOffset.FromUnixTimeSeconds(1783634303));
        using var server = await ShimTestServer.CreateAsync(backend);
        var client = server.GetTestClient();
        await RegisterRyukSessionAsync(client, ReaperSessionA);

        using var listRequest = RyukRequest(
            HttpMethod.Get,
            $"{listPath}?filters={ModernSessionFilters(ReaperSessionA)}");
        var listResponse = await client.SendAsync(listRequest);
        var body = await listResponse.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        Assert.Contains(resourceId, body);
        Assert.DoesNotContain("other-session", body);
        Assert.DoesNotContain("unlabelled", body);
        Assert.Contains(expectedTimestamp, body);
        Assert.DoesNotContain(char.ToLowerInvariant(expectedTimestamp[1]) + expectedTimestamp[2..], body);

        using var deleteRequest = RyukRequest(HttpMethod.Delete, deletePath);
        var deleteResponse = await client.SendAsync(deleteRequest);

        Assert.True(deleteResponse.StatusCode is HttpStatusCode.NoContent or HttpStatusCode.OK);
        Assert.Contains((resourceKind, resourceId), backend.DeletedResources);
    }

    [Theory]
    [InlineData("other-session")]
    [InlineData("unlabelled")]
    public async Task Ryuk_listener_rejects_filterless_delete_for_resources_outside_active_session(string resourceId)
    {
        var backend = new RecordingDockerBackend();
        backend.Resources[DockerResourceKind.Container]["matching"] = SessionResource("matching", ReaperSessionA);
        backend.Resources[DockerResourceKind.Container]["other-session"] = SessionResource("other-session", ReaperSessionB);
        backend.Resources[DockerResourceKind.Container]["unlabelled"] = new DockerResourceSnapshot(
            "unlabelled",
            new Dictionary<string, string>());
        using var server = await ShimTestServer.CreateAsync(backend);
        var client = server.GetTestClient();
        await RegisterRyukSessionAsync(client, ReaperSessionA);
        using var listRequest = RyukRequest(
            HttpMethod.Get,
            $"/containers/json?filters={ModernSessionFilters(ReaperSessionA)}");
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(listRequest)).StatusCode);

        using var deleteRequest = RyukRequest(HttpMethod.Delete, $"/containers/{resourceId}");
        var response = await client.SendAsync(deleteRequest);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(backend.DeletedContainers);
    }

    [Theory]
    [InlineData("/containers/json")]
    [InlineData("/networks")]
    [InlineData("/volumes")]
    [InlineData("/images/json")]
    public async Task Ryuk_listener_rejects_list_requests_without_resource_reaper_session_filter(string path)
    {
        var backend = new RecordingDockerBackend();
        backend.Resources[DockerResourceKind.Container]["container-1"] = new DockerResourceSnapshot(
            "container-1",
            new Dictionary<string, string>
            {
                ["org.testcontainers.resource-reaper-session"] = ReaperSessionA
            });
        using var server = await ShimTestServer.CreateAsync(backend);
        var client = server.GetTestClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add(HeaderListenerClassifier.ListenerHeaderName, "ryuk");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Ryuk_listener_rejects_a_well_formed_but_unregistered_session_filter()
    {
        var backend = new RecordingDockerBackend();
        using var server = await ShimTestServer.CreateAsync(backend);
        var client = server.GetTestClient();
        using var request = RyukRequest(
            HttpMethod.Get,
            $"/containers/json?filters={ModernSessionFilters(ReaperSessionA)}");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Ryuk_listener_does_not_allow_an_active_caller_to_switch_sessions()
    {
        var backend = new RecordingDockerBackend();
        using var server = await ShimTestServer.CreateAsync(backend);
        var client = server.GetTestClient();
        await RegisterRyukSessionAsync(client, ReaperSessionA);
        await RegisterRyukSessionAsync(client, ReaperSessionB);
        using var firstRequest = RyukRequest(
            HttpMethod.Get,
            $"/containers/json?filters={ModernSessionFilters(ReaperSessionA)}");
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(firstRequest)).StatusCode);

        using var switchedRequest = RyukRequest(
            HttpMethod.Get,
            $"/containers/json?filters={ModernSessionFilters(ReaperSessionB)}");
        var response = await client.SendAsync(switchedRequest);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private static DockerResourceSnapshot SessionResource(string id, string session)
    {
        return new DockerResourceSnapshot(
            id,
            new Dictionary<string, string>
            {
                ["org.testcontainers.resource-reaper-session"] = session
            },
            CreatedAt: DateTimeOffset.FromUnixTimeSeconds(1783634303));
    }

    private static void AssertProperties(JsonElement element, params string[] expectedProperties)
    {
        foreach (var property in expectedProperties)
        {
            Assert.True(element.TryGetProperty(property, out _), $"Expected property '{property}' was not present.");
            Assert.False(
                element.TryGetProperty(char.ToLowerInvariant(property[0]) + property[1..], out _),
                $"Camel-cased property '{char.ToLowerInvariant(property[0]) + property[1..]}' was present.");
        }
    }

    private static async Task RegisterRyukSessionAsync(HttpClient client, string session)
    {
        var response = await client.PostAsJsonAsync(
            $"/containers/create?name=testcontainers-ryuk-{session}",
            new DockerContainerCreateRequest
            {
                Image = "testcontainers/ryuk:0.14.0",
                Labels = new Dictionary<string, string>
                {
                    ["org.testcontainers"] = "true",
                    ["org.testcontainers.session-id"] = session,
                    ["org.testcontainers.resource-reaper-session"] = Guid.Empty.ToString("D")
                }
            });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    private static string ModernSessionFilters(string session)
    {
        var json = $$$"""{"label":{"org.testcontainers.resource-reaper-session={{{session}}}":true}}""";
        return Uri.EscapeDataString(json);
    }

    private static HttpRequestMessage RyukRequest(HttpMethod method, string path)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Add(HeaderListenerClassifier.ListenerHeaderName, "ryuk");
        return request;
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
