namespace Tnzi.Notification.Services;

/// <summary>
/// 使用HTTP REST API的短信发送服务实现（替代SDK方式）
/// 优点：无额外NuGet包依赖，更轻量级，统一使用IHttpClientFactory
/// </summary>
public class HttpSmsSender : ISmsSender
{
    private readonly NotificationOptions _options;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<HttpSmsSender> _logger;

    // Twilio API端点
    private const string TwilioApiBaseUrl = "https://api.twilio.com/2010-04-01";

    // Plivo API端点
    private const string PlivoApiBaseUrl = "https://api.plivo.com/v1";

    public HttpSmsSender(
        NotificationOptions options,
        IHttpClientFactory httpClientFactory,
        ILogger<HttpSmsSender> logger)
    {
        _options = Check.NotNull(options);
        _httpClientFactory = Check.NotNull(httpClientFactory);
        _logger = Check.NotNull(logger);
    }

    public async Task<SendResult> SendToAsync(string phoneNumber, string message, CancellationToken cancellationToken = default)
    {
        if (_options.SmsSender == null)
        {
            _logger.LogWarning("SMS sender options not configured");
            return SendResult.CreateFailure("SMS sender options not configured");
        }

        // In development, redirect all outbound SMS to the configured override number
        var devOverride = _options.SmsSender.DevOverridePhone;
        if (!string.IsNullOrWhiteSpace(devOverride))
        {
            _logger.LogWarning("[DEV] SMS redirected. OriginalTo={OriginalTo}, Override={Override}", phoneNumber, devOverride);
            message = $"[DEV → {phoneNumber}] {message}";
            phoneNumber = devOverride;
        }

        try
        {
            return _options.SmsSender.Provider.ToLower() switch
            {
                "twilio" => await SendViaTwilioHttpAsync(phoneNumber, message, cancellationToken),
                "plivo" => await SendViaPlivoHttpAsync(phoneNumber, message, cancellationToken),
                _ => SendResult.CreateFailure($"SMS provider '{_options.SmsSender.Provider}' is not supported. Supported providers: twilio, plivo")
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send SMS to {PhoneNumber}", phoneNumber);
            return SendResult.CreateFailure(ex.Message);
        }
    }

    /// <summary>
    /// 使用HTTP方式发送Twilio短信
    /// </summary>
    private async Task<SendResult> SendViaTwilioHttpAsync(string phoneNumber, string message, CancellationToken cancellationToken)
    {
        if (_options.SmsSender == null)
            throw new ConfigurationException("Notification:SmsSender", "SMS sender options not configured.");

        if (string.IsNullOrWhiteSpace(_options.SmsSender.TwilioAccountSid))
            throw new ConfigurationException("Notification:SmsSender:TwilioAccountSid", "Twilio Account SID is not configured.");
        if (string.IsNullOrWhiteSpace(_options.SmsSender.TwilioAuthToken))
            throw new ConfigurationException("Notification:SmsSender:TwilioAuthToken", "Twilio Auth Token is not configured.");
        if (string.IsNullOrWhiteSpace(_options.SmsSender.TwilioFromPhoneNumber))
            throw new ConfigurationException("Notification:SmsSender:TwilioFromPhoneNumber", "Twilio From Phone Number is not configured.");

        var accountSid = _options.SmsSender.TwilioAccountSid!;
        var authToken = _options.SmsSender.TwilioAuthToken!;
        var fromPhoneNumber = _options.SmsSender.TwilioFromPhoneNumber!;

        var httpClient = _httpClientFactory.CreateClient();

        // 构建请求URL
        var requestUrl = $"{TwilioApiBaseUrl}/Accounts/{accountSid}/Messages.json";

        // 构建表单数据
        var formData = new Dictionary<string, string>
        {
            { "From", fromPhoneNumber },
            { "To", phoneNumber },
            { "Body", message }
        };

        var formContent = new FormUrlEncodedContent(formData);

        // 构建Basic Auth Header（使用HttpRequestMessage避免修改HttpClient的DefaultRequestHeaders）
        var authBytes = Encoding.UTF8.GetBytes($"{accountSid}:{authToken}");
        var authHeader = Convert.ToBase64String(authBytes);
        using var request = new HttpRequestMessage(HttpMethod.Post, requestUrl)
        {
            Content = formContent
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", authHeader);

        try
        {
            var response = await httpClient.SendAsync(request, cancellationToken);
            var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                // 解析响应获取Message SID
                var responseJson = JsonDocument.Parse(responseContent);
                var messageSid = responseJson.RootElement.GetProperty("sid").GetString();

                _logger.LogInformation("SMS sent via Twilio HTTP to {PhoneNumber}, Status: {StatusCode}, SID: {MessageSid}",
                    phoneNumber, response.StatusCode, messageSid);

                return !string.IsNullOrWhiteSpace(messageSid)
                    ? SendResult.CreateSuccess(messageSid)
                    : SendResult.CreateFailure("Twilio API returned empty message SID");
            }
            else
            {
                _logger.LogError("Twilio HTTP API error: Status {StatusCode}, Response: {Response}",
                    response.StatusCode, responseContent);
                return SendResult.CreateFailure($"Twilio API error: {response.StatusCode} - {responseContent}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send SMS via Twilio HTTP to {PhoneNumber}", phoneNumber);
            return SendResult.CreateFailure(ex.Message);
        }
    }

    /// <summary>
    /// 使用HTTP方式发送Plivo短信
    /// </summary>
    private async Task<SendResult> SendViaPlivoHttpAsync(string phoneNumber, string message, CancellationToken cancellationToken)
    {
        if (_options.SmsSender == null)
            throw new ConfigurationException("Notification:SmsSender", "SMS sender options not configured.");

        if (string.IsNullOrWhiteSpace(_options.SmsSender.PlivoAuthId))
            throw new ConfigurationException("Notification:SmsSender:PlivoAuthId", "Plivo Auth ID is not configured.");
        if (string.IsNullOrWhiteSpace(_options.SmsSender.PlivoAuthToken))
            throw new ConfigurationException("Notification:SmsSender:PlivoAuthToken", "Plivo Auth Token is not configured.");
        if (string.IsNullOrWhiteSpace(_options.SmsSender.PlivoFromPhoneNumber))
            throw new ConfigurationException("Notification:SmsSender:PlivoFromPhoneNumber", "Plivo From Phone Number is not configured.");

        var authId = _options.SmsSender.PlivoAuthId!;
        var authToken = _options.SmsSender.PlivoAuthToken!;
        var fromPhoneNumber = _options.SmsSender.PlivoFromPhoneNumber!;

        var httpClient = _httpClientFactory.CreateClient();

        // 构建请求URL
        var requestUrl = $"{PlivoApiBaseUrl}/Account/{authId}/Message/";

        // 构建JSON请求体
        var requestBody = new
        {
            src = fromPhoneNumber,
            dst = phoneNumber,
            text = message
        };

        var jsonContent = JsonSerializer.Serialize(requestBody, new JsonSerializerOptions
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        });
        var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

        // 构建Basic Auth Header（使用HttpRequestMessage避免修改HttpClient的DefaultRequestHeaders）
        var authBytes = Encoding.UTF8.GetBytes($"{authId}:{authToken}");
        var authHeader = Convert.ToBase64String(authBytes);
        using var request = new HttpRequestMessage(HttpMethod.Post, requestUrl)
        {
            Content = content
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", authHeader);

        try
        {
            var response = await httpClient.SendAsync(request, cancellationToken);
            var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                // 解析响应获取Message UUID
                var responseJson = JsonDocument.Parse(responseContent);
                var messageUuids = responseJson.RootElement.GetProperty("message_uuid").EnumerateArray();

                if (messageUuids.Any())
                {
                    var messageUuid = messageUuids.First().GetString();
                    _logger.LogInformation("SMS sent via Plivo HTTP to {PhoneNumber}, Message UUID: {MessageUuid}",
                        phoneNumber, messageUuid);

                    if (!string.IsNullOrWhiteSpace(messageUuid))
                    {
                        return SendResult.CreateSuccess(messageUuid);
                    }
                }

                return SendResult.CreateFailure("Plivo API returned empty message UUID");
            }
            else
            {
                _logger.LogError("Plivo HTTP API error: Status {StatusCode}, Response: {Response}",
                    response.StatusCode, responseContent);
                return SendResult.CreateFailure($"Plivo API error: {response.StatusCode} - {responseContent}");
            }
        }
        catch (Exception ex)
        {
            // 未知异常使用Error级别
            _logger.LogError(ex, "Failed to send SMS via Plivo HTTP to {PhoneNumber}", phoneNumber);
            return SendResult.CreateFailure(ex.Message);
        }
    }
}

