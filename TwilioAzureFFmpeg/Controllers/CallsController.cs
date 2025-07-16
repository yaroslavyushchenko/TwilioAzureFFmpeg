using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Twilio.TwiML;
using Twilio.TwiML.Voice;
using Task = System.Threading.Tasks.Task;
using Stream = Twilio.TwiML.Voice.Stream;

namespace TwilioAzureFFmpeg.Controllers;

[ApiController]
[Route("callback/[controller]")]
public class CallsController : ControllerBase
{
    [HttpPost("voice")]
    public async Task<IActionResult> VoiceCall(CancellationToken cancellationToken)
    {
        var response = new VoiceResponse();
        var connect = new Connect();
        var stream = new Stream(url: $"wss://{Request.Host}/stream/");
        connect.Append(stream);
        response.Append(connect);

        return Content(response.ToString(), "text/xml");
    }

    [HttpGet("/stream")]
    public async Task Get()
    {
        if (HttpContext.WebSockets.IsWebSocketRequest)
        {
            using var webSocket = await HttpContext.WebSockets.AcceptWebSocketAsync();
            await HandleTwilioMediaStream(webSocket);
        }
        else
        {
            HttpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
        }
    }

    private async Task HandleTwilioMediaStream(WebSocket webSocket)
    {
        var buffer = new byte[1024 * 4];

        try
        {
            while (webSocket.State == WebSocketState.Open)
            {
                var result = await webSocket.ReceiveAsync(
                    new ArraySegment<byte>(buffer),
                    CancellationToken.None);

                if (result.MessageType == WebSocketMessageType.Text)
                {
                    var message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    await ProcessTwilioMessage(webSocket, message);
                }
                else if (result.MessageType == WebSocketMessageType.Close)
                {
                    await webSocket.CloseAsync(
                        WebSocketCloseStatus.NormalClosure,
                        "Connection closed by client",
                        CancellationToken.None);
                    break;
                }
            }
        }
        catch (WebSocketException ex)
        {
            Console.WriteLine($"WebSocket error: {ex.Message}");
        }
    }

    private async Task ProcessTwilioMessage(WebSocket webSocket, string message)
    {
        try
        {
            using var document = JsonDocument.Parse(message);
            var root = document.RootElement;

            if (!root.TryGetProperty("event", out var eventElement))
                return;

            var eventType = eventElement.GetString();

            switch (eventType)
            {
                case "connected":
                    await HandleConnected(webSocket, root);
                    break;
                case "start":
                    await HandleStart(webSocket, root);
                    break;
                case "media":
                    await HandleMedia(webSocket, root);
                    break;
                case "stop":
                    await HandleStop(webSocket, root);
                    break;
                default:
                    Console.WriteLine($"Unknown event: {eventType}");
                    break;
            }
        }
        catch (JsonException ex)
        {
            Console.WriteLine($"JSON parsing error: {ex.Message}");
        }
    }

    private async Task HandleConnected(WebSocket webSocket, JsonElement root)
    {
        Console.WriteLine("WebSocket Connected to Twilio");

        if (root.TryGetProperty("protocol", out var protocol))
        {
            Console.WriteLine($"Protocol: {protocol.GetString()}");
        }

        if (root.TryGetProperty("version", out var version))
        {
            Console.WriteLine($"Version: {version.GetString()}");
        }
    }

    private async Task HandleStart(WebSocket webSocket, JsonElement root)
    {
        Console.WriteLine("Media stream started");

        if (root.TryGetProperty("streamSid", out var streamSid))
        {
            Console.WriteLine($"Stream SID: {streamSid.GetString()}");
        }

        if (root.TryGetProperty("start", out var start))
        {
            if (start.TryGetProperty("accountSid", out var accountSid))
                Console.WriteLine($"Account SID: {accountSid.GetString()}");

            if (start.TryGetProperty("callSid", out var callSid))
                Console.WriteLine($"Call SID: {callSid.GetString()}");

            if (start.TryGetProperty("mediaFormat", out var mediaFormat))
            {
                if (mediaFormat.TryGetProperty("encoding", out var encoding))
                    Console.WriteLine($"Encoding: {encoding.GetString()}");

                if (mediaFormat.TryGetProperty("sampleRate", out var sampleRate))
                    Console.WriteLine($"Sample Rate: {sampleRate.GetInt32()}");

                if (mediaFormat.TryGetProperty("channels", out var channels))
                    Console.WriteLine($"Channels: {channels.GetInt32()}");
            }
        }
    }

    private async Task HandleMedia(WebSocket webSocket, JsonElement root)
    {
        if (root.TryGetProperty("media", out var media))
        {
            if (media.TryGetProperty("track", out var track))
            {
                var trackValue = track.GetString();
                Console.WriteLine($"Track: {trackValue}");
            }

            if (media.TryGetProperty("chunk", out var chunk))
            {
                var chunkValue = chunk.GetString();
                Console.WriteLine($"Chunk: {chunkValue}");
            }

            if (media.TryGetProperty("timestamp", out var timestamp))
            {
                var timestampValue = timestamp.GetString();
                Console.WriteLine($"Timestamp: {timestampValue}");
            }

            if (media.TryGetProperty("payload", out var payload))
            {
                var payloadValue = payload.GetString();
                await ProcessAudioData(webSocket, payloadValue, media);
            }
        }
    }

    private async Task ProcessAudioData(WebSocket webSocket, string base64Audio, JsonElement media)
    {
        try
        {
            byte[] audioBytes = Convert.FromBase64String(base64Audio);

            Console.WriteLine($"Received audio data: {audioBytes.Length} bytes");

            await SendAudioBack(webSocket, base64Audio, media);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error processing audio: {ex.Message}");
        }
    }

    private async Task SendAudioBack(WebSocket webSocket, string audioData, JsonElement originalMedia)
    {
        try
        {
            var responseMedia = new
            {
                @event = "media",
                streamSid = GetStreamSid(originalMedia),
                media = new
                {
                    track = "outbound",
                    chunk = GetChunk(originalMedia),
                    timestamp = GetTimestamp(originalMedia),
                    payload = audioData 
                }
            };

            var json = JsonSerializer.Serialize(responseMedia);
            var bytes = Encoding.UTF8.GetBytes(json);

            await webSocket.SendAsync(
                new ArraySegment<byte>(bytes),
                WebSocketMessageType.Text,
                true,
                CancellationToken.None);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error sending audio back: {ex.Message}");
        }
    }

    private async Task HandleStop(WebSocket webSocket, JsonElement root)
    {
        Console.WriteLine("Media stream stopped");

        if (root.TryGetProperty("streamSid", out var streamSid))
        {
            Console.WriteLine($"Stopped Stream SID: {streamSid.GetString()}");
        }
    }

    private string GetStreamSid(JsonElement media)
    {
        var parent = media;
        while (parent.ValueKind != JsonValueKind.Undefined)
        {
            if (parent.TryGetProperty("streamSid", out var streamSid))
                return streamSid.GetString() ?? "";
            break;
        }

        return "";
    }

    private string GetChunk(JsonElement media)
    {
        if (media.TryGetProperty("chunk", out var chunk))
            return chunk.GetString() ?? "";
        return "";
    }

    private string GetTimestamp(JsonElement media)
    {
        if (media.TryGetProperty("timestamp", out var timestamp))
            return timestamp.GetString() ?? "";
        return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
    }

    private async Task SendMark(WebSocket webSocket, string streamSid, string markName)
    {
        var markMessage = new
        {
            @event = "mark",
            streamSid = streamSid,
            mark = new { name = markName }
        };

        var json = JsonSerializer.Serialize(markMessage);
        var bytes = Encoding.UTF8.GetBytes(json);

        await webSocket.SendAsync(
            new ArraySegment<byte>(bytes),
            WebSocketMessageType.Text,
            true,
            CancellationToken.None);
    }

    private async Task ClearBuffer(WebSocket webSocket, string streamSid)
    {
        var clearMessage = new
        {
            @event = "clear",
            streamSid = streamSid
        };

        var json = JsonSerializer.Serialize(clearMessage);
        var bytes = Encoding.UTF8.GetBytes(json);

        await webSocket.SendAsync(
            new ArraySegment<byte>(bytes),
            WebSocketMessageType.Text,
            true,
            CancellationToken.None);
    }
}