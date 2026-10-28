namespace Tnzi.Payment.Stripe.Providers;

/// <summary>
/// Stripe支付渠道实现
/// </summary>
/// <remarks>
/// 厂商命名空间 <c>Stripe</c> 在本程序集里被 <c>Tnzi.Payment.Stripe</c> 遮蔽，
/// 需要显式限定厂商类型时写 <c>global::Stripe.X</c>，理由见 GlobalUsings.cs。
/// </remarks>
public class StripeProvider : IPaymentProvider
{
    private readonly IOptions<StripeOptions> _options;
    private readonly ILogger<StripeProvider> _logger;

    public string ChannelCode => PaymentConstants.StripeChannelCode;
    public string ChannelName => PaymentConstants.StripeChannelCode;

    public StripeProvider(IOptions<StripeOptions> options, ILogger<StripeProvider> logger)
    {
        _options = Check.NotNull(options);
        _logger = Check.NotNull(logger);
    }

    /// <summary>
    /// 取 Stripe 客户端。缓存在工厂里而不是本实例上：本类是 Scoped，
    /// 存在实例字段上等于每个请求重建一次连接池。
    /// </summary>
    private StripeClient GetClient() => StripeClientFactory.Create(_options.Value);

    /// <summary>
    /// 幂等键：同一业务流水重放到 Stripe 时不会产生第二笔真实扣款/退款。
    /// 这是把"网络超时后重试"从资损风险降级为安全操作的关键，成本仅一个请求头。
    /// </summary>
    private static RequestOptions Idempotent(string scope, string businessNo)
        => new() { IdempotencyKey = $"{scope}:{businessNo}" };

    /// <inheritdoc />
    public string? PublishableKey => string.IsNullOrWhiteSpace(_options.Value.PublishableKey) ? null : _options.Value.PublishableKey;

    public bool IsSupported(PaymentMethodEnum method)
    {
        return method switch
        {
            PaymentMethodEnum.CreditCard or PaymentMethodEnum.DebitCard or PaymentMethodEnum.ApplePay or PaymentMethodEnum.GooglePay => true,
            _ => false
        };
    }

    public async Task<Result<PaymentProviderOrderResult>> CreatePaymentAsync(PaymentProviderCreateDto input)
    {
        try
        {
            var client = GetClient();
            var service = new PaymentIntentService(client);

            // 付款同时存卡：卡必须挂在一个客户名下才能留给之后的无人值守扣款，没有就先为这个账户建一个
            var customerId = input.ProviderCustomerId;
            if (input.SavePaymentMethod && string.IsNullOrWhiteSpace(customerId))
            {
                var account = input.BillingAccount
                    ?? throw new InvalidOperationException("Saving a payment method requires the payment's billing account.");
                customerId = await CreateCustomerAsync(client, account, input.CustomerName, input.CustomerEmail);
            }

            var options = new PaymentIntentCreateOptions
            {
                Amount = CurrencyInfo.ToMinorUnits(input.Amount, input.Currency),
                Currency = input.Currency.ToLowerInvariant(),
                Customer = customerId,
                Metadata = BuildIntentMetadata(input.TradeNo, input.BusinessOrderNo, offSession: false, _options.Value),
                StatementDescriptorSuffix = StatementDescriptorSuffix,
                // off_session：这张卡会被留下来做之后的无人值守续费，Stripe 据此在这一次（有人在场、可做 3-D Secure）就完成授权
                SetupFutureUsage = input.SavePaymentMethod ? "off_session" : null
            };

            if (input.ReusableMethodsOnly)
            {
                // 只收卡（与绑卡的 SetupIntent 同一个参数）：先买后付这类方式不能留给之后的续费，
                // 不在单据上限制，Payment Element 会照账户后台启用的方式把 Klarna 一并提供出来
                options.AllowedPaymentMethodTypes = ["card"];
            }
            else
            {
                options.AutomaticPaymentMethods = new PaymentIntentAutomaticPaymentMethodsOptions { Enabled = true };
            }

            var paymentIntent = await service.CreateAsync(options, Idempotent("pi", input.TradeNo));

            _logger.LogInformation("Stripe payment intent created. TradeNo: {TradeNo}, IntentId: {IntentId}",
                input.TradeNo, paymentIntent.Id);

            return Result.Success(new PaymentProviderOrderResult
            {
                TradeNo = input.TradeNo,
                ExternalTradeNo = paymentIntent.Id,
                PayParams = paymentIntent.ClientSecret,
                ExpireTime = input.ExpireTime,
            });
        }
        catch (StripeException ex)
        {
            _logger.LogError(ex, "Stripe payment creation failed. TradeNo: {TradeNo}", input.TradeNo);
            return Result.Failure<PaymentProviderOrderResult>(ErrorCodes.StripePaymentFailed, 400);
        }
    }

    /// <summary>
    /// 按本地流水号（写在 PaymentIntent 的 <c>metadata.TradeNo</c> 上）搜索渠道侧的支付；
    /// <see cref="SyncOrderAsync"/> 拿到的不是 PaymentIntent id 时走这里。
    /// </summary>
    private async Task<Result<PaymentProviderQueryResult>> SearchByTradeNoAsync(string tradeNo)
    {
        try
        {
            var client = GetClient();
            var service = new PaymentIntentService(client);

            var searchResult = await service.SearchAsync(new PaymentIntentSearchOptions
            {
                Query = BuildTradeNoSearchQuery(tradeNo, _options.Value)
            });

            var intent = searchResult.Data.FirstOrDefault();
            if (intent == null)
            {
                return Result.Failure<PaymentProviderQueryResult>("Payment intent not found.");
            }

            return Result.Success(BuildQueryResult(intent, tradeNo));
        }
        catch (StripeException ex)
        {
            _logger.LogError(ex, "Stripe payment query failed. TradeNo: {TradeNo}", tradeNo);
            return Result.Failure<PaymentProviderQueryResult>(ErrorCodes.StripePaymentQueryFailed, 400);
        }
    }

    public async Task<Result<PaymentProviderRefundResult>> RefundAsync(PaymentProviderRefundDto input)
    {
        try
        {
            var client = GetClient();
            var service = new global::Stripe.RefundService(client);

            var refundOptions = new RefundCreateOptions
            {
                PaymentIntent = input.ExternalTradeNo ?? input.TradeNo,
                Amount = CurrencyInfo.ToMinorUnits(input.RefundAmount, input.Currency),
                Reason = RefundReasons.RequestedByCustomer,
                // 退款号写进元数据：渠道回调（charge.refunded / refund.updated）据此认出这是本模块发起的那一笔，
                // 只推进它自己的状态，而不是当成一笔在 Stripe 后台手工退的新退款再记一遍
                Metadata = WithAppStamp(new Dictionary<string, string> { [RefundNoMetadataKey] = input.RefundNo }, _options.Value)
            };

            var refund = await service.CreateAsync(refundOptions, Idempotent("re", input.RefundNo));

            _logger.LogInformation("Stripe refund created. RefundNo: {RefundNo}, Amount: {Amount}, Status: {Status}",
                input.RefundNo, input.RefundAmount, refund.Status);

            return Result.Success(new PaymentProviderRefundResult
            {
                RefundNo = input.RefundNo,
                ExternalRefundNo = refund.Id,
                RefundAmount = CurrencyInfo.FromMinorUnits(refund.Amount, input.Currency),
                Status = MapStripeRefundStatus(refund.Status),
                CompletedTime = refund.Status == "succeeded" ? DateTime.UtcNow : null
            });
        }
        catch (StripeException ex)
        {
            _logger.LogError(ex, "Stripe refund failed. RefundNo: {RefundNo}", input.RefundNo);
            return Result.Failure<PaymentProviderRefundResult>(ErrorCodes.StripeRefundFailed, 400);
        }
    }

    public async Task<Result<PaymentProviderRefundQueryResult>> QueryRefundAsync(string externalRefundNo)
    {
        try
        {
            var client = GetClient();
            var service = new global::Stripe.RefundService(client);

            var refund = await service.GetAsync(externalRefundNo);

            return Result.Success(new PaymentProviderRefundQueryResult
            {
                RefundNo = externalRefundNo,
                ExternalRefundNo = refund.Id,
                Status = MapStripeRefundStatus(refund.Status),
                RefundAmount = CurrencyInfo.FromMinorUnits(refund.Amount, refund.Currency),
                CompletedTime = refund.Status == "succeeded" ? DateTime.UtcNow : null
            });
        }
        catch (StripeException ex)
        {
            _logger.LogError(ex, "Stripe refund query failed. ExternalRefundNo: {ExternalRefundNo}", externalRefundNo);
            return Result.Failure<PaymentProviderRefundQueryResult>(ErrorCodes.StripeRefundQueryFailed, 400);
        }
    }

    public Task<Result<PaymentProviderCallbackResult>> HandleCallbackAsync(IDictionary<string, string> parameters)
    {
        if (!parameters.TryGetValue(PaymentConstants.CallbackRawBodyKey, out var rawBody) || string.IsNullOrWhiteSpace(rawBody))
            return Task.FromResult(Result.Failure<PaymentProviderCallbackResult>(ErrorCodes.PaymentInvalidSignature, 400));

        try
        {
            // 版本容错的理由见 VerifySignatureAsync：签名已在前一步验过，这里只负责读字段
            var stripeEvent = EventUtility.ParseEvent(rawBody, throwOnApiVersionMismatch: false);
            WarnOnApiVersionMismatch(stripeEvent);

            // 渠道侧的退款与争议：不带本模块的流水号，服务层按 PaymentIntent 找本地支付，找不到即是别人的、安静地成功
            if (stripeEvent.Data.Object is global::Stripe.Charge charge && stripeEvent.Type == "charge.refunded")
                return MapChargeRefundedAsync(stripeEvent, charge);

            if (stripeEvent.Data.Object is global::Stripe.Refund refundObject && stripeEvent.Type.StartsWith("refund.", StringComparison.Ordinal))
                return Task.FromResult(Result.Success(MapRefundEvent(stripeEvent, [refundObject], refundObject.PaymentIntentId)));

            if (stripeEvent.Data.Object is global::Stripe.Dispute dispute && stripeEvent.Type.StartsWith("charge.dispute.", StringComparison.Ordinal))
                return Task.FromResult(Result.Success(MapDisputeEvent(stripeEvent, dispute)));

            // 支付方式事件不带本应用的元数据（卡由 Stripe Elements 建），按事件类型分流，不按应用标记过滤：
            // 本地按凭据查找，别的应用的卡在这里本来就查不到，查不到即安静地成功。
            if (stripeEvent.Data.Object is StripePaymentMethod paymentMethod)
                return Task.FromResult(Result.Success(MapPaymentMethodEvent(stripeEvent, paymentMethod)));

            // Stripe 会推送大量与支付状态无关的事件（customer.*、invoice.* 等）。
            // 这类事件必须被识别为"已接收但无需处理"，否则回非 2xx 会让 Stripe 反复重投直至禁用端点。
            if (stripeEvent.Data.Object is not PaymentIntent paymentIntent)
            {
                return Task.FromResult(Result.Success(new PaymentProviderCallbackResult
                {
                    EventId = stripeEvent.Id,
                    IsHandled = false
                }));
            }

            // ★ 同一个 Stripe 账户被几个应用共用：不带本应用标记的 intent 是别人的。安静地答 200 忽略，
            // 不记告警（那是别人的正常业务），更不去本地找支付（找不到会答 404，Stripe 随即反复重投三天）。
            if (!CarriesAppStamp(paymentIntent.Metadata, _options.Value))
            {
                _logger.LogDebug("Stripe event {EventId} for payment intent {IntentId} belongs to another application; ignored.",
                    stripeEvent.Id, paymentIntent.Id);
                return Task.FromResult(Result.Success(new PaymentProviderCallbackResult
                {
                    EventId = stripeEvent.Id,
                    IsHandled = false
                }));
            }

            if (!paymentIntent.Metadata.TryGetValue("TradeNo", out var tradeNo) || string.IsNullOrWhiteSpace(tradeNo))
            {
                // 非本系统发起的 PaymentIntent（如在 Stripe 后台手工创建）同样不该拖垮 webhook
                _logger.LogWarning("Stripe callback payment intent {IntentId} has no TradeNo metadata; ignored.", paymentIntent.Id);
                return Task.FromResult(Result.Success(new PaymentProviderCallbackResult
                {
                    EventId = stripeEvent.Id,
                    IsHandled = false
                }));
            }

            return Task.FromResult(Result.Success(new PaymentProviderCallbackResult
            {
                TradeNo = tradeNo,
                ExternalTradeNo = paymentIntent.Id,
                Status = MapCallbackStatus(stripeEvent.Type, paymentIntent),
                // 金额已按这个币种从最小单位换算回来；不把它带上，服务层就只能比数值
                Currency = paymentIntent.Currency,
                PaidAmount = paymentIntent.AmountReceived > 0
                    ? CurrencyInfo.FromMinorUnits(paymentIntent.AmountReceived, paymentIntent.Currency)
                    : CurrencyInfo.FromMinorUnits(paymentIntent.Amount, paymentIntent.Currency),
                FailReason = paymentIntent.LastPaymentError?.Message,
                EventId = stripeEvent.Id,
                SavedPaymentMethodToken = SavedPaymentMethodOf(paymentIntent)
            }));
        }
        catch (StripeException ex)
        {
            _logger.LogWarning(ex, "Stripe callback parse failed.");
            return Task.FromResult(Result.Failure<PaymentProviderCallbackResult>(ErrorCodes.PaymentInvalidSignature, 400));
        }
    }

    public Task<bool> VerifySignatureAsync(IDictionary<string, string> parameters)
    {
        if (string.IsNullOrWhiteSpace(_options.Value.WebhookSecret))
            return Task.FromResult(false);

        if (!parameters.TryGetValue(PaymentConstants.CallbackRawBodyKey, out var rawBody) || string.IsNullOrWhiteSpace(rawBody))
            return Task.FromResult(false);

        if (!parameters.TryGetValue(PaymentConstants.CallbackStripeSignatureKey, out var signature) || string.IsNullOrWhiteSpace(signature))
            return Task.FromResult(false);

        // 只验真伪（HMAC + 时间戳容差），不连带检查事件的 API 版本。
        // ConstructEvent 默认在事件版本与 SDK 发布线不一致时也抛 StripeException，
        // 读成「签名无效」的后果是端点版本没对齐时**每一条**真回调都被 400 拒掉、Stripe 重投直至禁用端点，
        // 而日志说的是签名不对。版本不一致在解析时单独告警，见 WarnOnApiVersionMismatch。
        try
        {
            EventUtility.ValidateSignature(rawBody, signature, _options.Value.WebhookSecret);
            return Task.FromResult(true);
        }
        catch (StripeException ex)
        {
            _logger.LogWarning(ex, "Stripe webhook signature verification failed.");
            return Task.FromResult(false);
        }
    }

    /// <summary>
    /// 事件的 API 版本与 SDK 不在同一条发布线时记一条告警，指名两边的版本与对齐方法。
    /// </summary>
    /// <remarks>
    /// 不拒收：本渠道读的字段（事件 id/type、PaymentIntent 的 id/status/金额/币种/metadata/失败原因、
    /// PaymentMethod id）跨发布线稳定。拒收换来的是丢掉所有失败、作废与撤卡事件，
    /// 而支付本身只能等过期扫描去渠道查才被记成功。
    /// </remarks>
    private void WarnOnApiVersionMismatch(Event stripeEvent)
    {
        if (string.IsNullOrWhiteSpace(stripeEvent.ApiVersion) || EventUtility.IsCompatibleApiVersion(stripeEvent.ApiVersion))
            return;

        _logger.LogWarning(
            "Stripe event {EventId} uses API version {EventApiVersion}, but Stripe.net expects {SdkApiVersion}. "
            + "The event is processed; set the webhook endpoint's API version to {SdkApiVersion} in the Stripe dashboard to align them.",
            stripeEvent.Id, stripeEvent.ApiVersion, StripeConfiguration.ApiVersion, StripeConfiguration.ApiVersion);
    }

    public async Task<Result<PaymentProviderQueryResult>> SyncOrderAsync(string tradeNo)
    {
        // PaymentService 传入 ExternalTradeNo（Stripe PaymentIntent ID，形如 pi_xxx）
        // 直接通过 ID 获取比 metadata 搜索更快更可靠
        if (tradeNo.StartsWith("pi_", StringComparison.Ordinal))
        {
            try
            {
                var client = GetClient();
                var service = new PaymentIntentService(client);
                var intent = await service.GetAsync(tradeNo);

                return Result.Success(BuildQueryResult(intent, tradeNo));
            }
            catch (StripeException ex)
            {
                _logger.LogError(ex, "Stripe sync by PaymentIntent ID failed. Id: {Id}", tradeNo);
                return Result.Failure<PaymentProviderQueryResult>(ErrorCodes.StripePaymentQueryFailed, 400);
            }
        }

        // Fallback: 按内部 TradeNo 通过 metadata 搜索
        return await SearchByTradeNoAsync(tradeNo);
    }

    public bool SupportsPaymentCancellation => true;

    /// <summary>
    /// 作废 PaymentIntent。已作废 / 不存在视为成功（幂等）；**已付掉必须失败**（服务层随后把它记成功而不是关单）。
    /// </summary>
    /// <remarks>
    /// Stripe 对状态不允许作废的 intent 抛 <c>payment_intent_unexpected_state</c>，错误体里带着当前的 intent：
    /// 以它的状态区分「已经死了」与「已经付了」，不用再查一次。
    /// PaymentIntent 不会自己过期（Checkout Session 才会）：本地过期而不作废它，付款人几天后打开旧收银台照样付得进去。
    /// </remarks>
    public async Task<Result> CancelPaymentAsync(string tradeNo)
    {
        try
        {
            var service = new PaymentIntentService(GetClient());
            var intentId = tradeNo;
            if (!tradeNo.StartsWith("pi_", StringComparison.Ordinal))
            {
                // 落库前进程死掉的单只有内部 TradeNo：按元数据找它，找不到 = 渠道侧没有可付的东西
                var found = await service.SearchAsync(new PaymentIntentSearchOptions
                {
                    Query = BuildTradeNoSearchQuery(tradeNo, _options.Value)
                });
                var match = found.Data.FirstOrDefault();
                if (match == null)
                    return Result.Success();
                intentId = match.Id;
            }

            var cancelled = await service.CancelAsync(intentId, new PaymentIntentCancelOptions { CancellationReason = "abandoned" });
            return string.Equals(cancelled.Status, "canceled", StringComparison.Ordinal)
                ? Result.Success()
                : Result.Failure(ErrorCodes.StripePaymentCancelFailed, 400);
        }
        catch (StripeException ex) when (ex.HttpStatusCode == HttpStatusCode.NotFound)
        {
            _logger.LogInformation("Stripe payment intent {Id} no longer exists; nothing to void.", tradeNo);
            return Result.Success();
        }
        catch (StripeException ex) when (ex.StripeError?.PaymentIntent?.Status == "canceled")
        {
            return Result.Success();
        }
        catch (StripeException ex)
        {
            _logger.LogWarning(ex, "Stripe payment intent {Id} could not be voided (intent status {Status}).",
                tradeNo, ex.StripeError?.PaymentIntent?.Status);
            return Result.Failure(ErrorCodes.StripePaymentCancelFailed, 400);
        }
    }

    public async Task<Result<PaymentParamsDto>> GetPaymentParamsAsync(string tradeNo)
    {
        // ClientSecret 只在建单那一刻随创建结果返回过一次，用户刷新收银台就丢了。
        // 这里按 PaymentIntent ID 回源取，让收银台可恢复（Stripe 的 ClientSecret 本就是发给前端的）。
        if (!tradeNo.StartsWith("pi_", StringComparison.Ordinal))
        {
            return Result.Success(new PaymentParamsDto { TradeNo = tradeNo });
        }

        try
        {
            var client = GetClient();
            var service = new PaymentIntentService(client);
            var intent = await service.GetAsync(tradeNo);

            return Result.Success(new PaymentParamsDto
            {
                TradeNo = intent.Metadata.TryGetValue("TradeNo", out var internalNo) ? internalNo : tradeNo,
                ClientSecret = intent.ClientSecret,
                AvailableMethods = intent.PaymentMethodTypes?.ToList() ?? []
            });
        }
        catch (StripeException ex)
        {
            _logger.LogWarning(ex, "Stripe payment params fetch failed. Id: {Id}", tradeNo);
            return Result.Failure<PaymentParamsDto>(ErrorCodes.StripePaymentQueryFailed, 400);
        }
    }

    public bool SupportsOffSessionCharge => true;

    public bool SupportsPaymentMethodStorage => true;

    public async Task<Result<PaymentProviderSetupResult>> CreateSetupSessionAsync(PaymentProviderSetupDto input)
    {
        try
        {
            var client = GetClient();
            var customerId = input.ProviderCustomerId;

            var account = input.BillingAccount ?? BillingAccountRef.ForUser(input.UserId);
            if (string.IsNullOrWhiteSpace(customerId))
            {
                customerId = await CreateCustomerAsync(client, account, input.CustomerName, input.CustomerEmail);
            }

            var setupIntent = await new SetupIntentService(client).CreateAsync(new SetupIntentCreateOptions
            {
                Customer = customerId,
                // off_session：声明该支付方式将用于后续无人值守扣款，Stripe 会据此在收集阶段完成必要的授权
                Usage = "off_session",
                // 只收卡。API 2026-08-26 起写入 payment_method_types 一律 400（payment_method_types_no_longer_supported），
                // allowed_payment_method_types 是它的替代：Stripe 只在这份清单里挑要提供的方式（与账户后台启用的方式一起决定），
                // 不兼容的类型被滤掉而不是报错。
                // SetupIntent 对象上的 payment_method_types 仍在，只是变成了只读的计算结果。
                AllowedPaymentMethodTypes = ["card"],
                Metadata = WithAppStamp(new Dictionary<string, string>
                {
                    { "UserId", input.UserId.ToString() },
                    { BillingAccountMetadataKey, account.Key }
                }, _options.Value)
            });

            _logger.LogInformation("Stripe setup intent created. UserId: {UserId}, SetupIntentId: {SetupIntentId}",
                input.UserId, setupIntent.Id);

            return Result.Success(new PaymentProviderSetupResult
            {
                SetupId = setupIntent.Id,
                ClientSecret = setupIntent.ClientSecret,
                ProviderCustomerId = customerId
            });
        }
        catch (StripeException ex)
        {
            _logger.LogError(ex, "Stripe setup intent creation failed. UserId: {UserId}", input.UserId);
            return Result.Failure<PaymentProviderSetupResult>(ErrorCodes.PaymentMethodBindingFailed, 400);
        }
    }

    public async Task<Result<PaymentProviderPaymentMethodResult>> ResolvePaymentMethodAsync(PaymentProviderResolveMethodDto input)
    {
        if (string.IsNullOrWhiteSpace(input.PaymentMethodToken))
            return Result.Failure<PaymentProviderPaymentMethodResult>(ErrorCodes.PaymentMethodNotFound, 400);

        try
        {
            var client = GetClient();
            var service = new global::Stripe.PaymentMethodService(client);

            var paymentMethod = await service.GetAsync(input.PaymentMethodToken);

            var customerId = input.ProviderCustomerId;
            if (string.IsNullOrWhiteSpace(customerId) && !string.IsNullOrWhiteSpace(paymentMethod.CustomerId))
            {
                // ★ 本地还没有渠道客户（首次绑卡），而 token 已经挂在某个 customer 名下。
                //   此前这里直接把那个 customer 当作比对基准，下面再拿它与 paymentMethod.CustomerId 比 —— 恒等，
                //   归属守卫在这条路径上自我抵消：知道别人 pm_ 标识的用户绑定成功，且从此以卡主的 customer 续费扣款。
                //   合法的首次绑卡到这里时 pm 也已经带着 customer（SetupIntent 建在新建客户名下，Stripe 确认时就挂上了），
                //   所以不能要求「未挂任何 customer」；判据是那个 customer 是不是本系统为**这个用户**建的：
                //   CreateCustomerAsync 写了 Metadata["UserId"]。缺失或不等一律拒绝，方向关闭。
                var customer = await new CustomerService(client).GetAsync(paymentMethod.CustomerId);
                var violation = DescribeCustomerOwnershipViolation(customer, input.BillingAccount ?? BillingAccountRef.ForUser(input.UserId), _options.Value);
                if (violation != null)
                {
                    _logger.LogWarning(
                        "Stripe payment method {PaymentMethodId} is attached to customer {CustomerId}, which is not the caller's ({UserId}): {Reason}",
                        paymentMethod.Id, paymentMethod.CustomerId, input.UserId, violation);
                    return Result.Failure<PaymentProviderPaymentMethodResult>(ErrorCodes.PaymentMethodBindingFailed, 403);
                }

                customerId = paymentMethod.CustomerId;
            }

            if (string.IsNullOrWhiteSpace(customerId))
            {
                customerId = await CreateCustomerAsync(client, input.BillingAccount ?? BillingAccountRef.ForUser(input.UserId), input.CustomerName, input.CustomerEmail);
            }

            // 未挂到客户名下的支付方式无法用于 off-session 扣款，这里补齐绑定（已绑定则跳过）
            if (!string.Equals(paymentMethod.CustomerId, customerId, StringComparison.Ordinal))
            {
                if (!string.IsNullOrWhiteSpace(paymentMethod.CustomerId))
                {
                    _logger.LogWarning(
                        "Stripe payment method {PaymentMethodId} belongs to customer {ActualCustomer}, expected {ExpectedCustomer}.",
                        paymentMethod.Id, paymentMethod.CustomerId, customerId);
                    return Result.Failure<PaymentProviderPaymentMethodResult>(ErrorCodes.PaymentMethodBindingFailed, 400);
                }

                paymentMethod = await service.AttachAsync(paymentMethod.Id, new PaymentMethodAttachOptions
                {
                    Customer = customerId
                });
            }

            return Result.Success(new PaymentProviderPaymentMethodResult
            {
                Token = paymentMethod.Id,
                ProviderCustomerId = customerId,
                MethodType = MapStripeMethodType(paymentMethod),
                Brand = paymentMethod.Card?.Brand,
                Last4 = paymentMethod.Card?.Last4,
                ExpiryMonth = paymentMethod.Card != null ? (int)paymentMethod.Card.ExpMonth : null,
                ExpiryYear = paymentMethod.Card != null ? (int)paymentMethod.Card.ExpYear : null
            });
        }
        catch (StripeException ex)
        {
            _logger.LogError(ex, "Stripe payment method resolution failed. Token: {Token}", input.PaymentMethodToken);
            return Result.Failure<PaymentProviderPaymentMethodResult>(ErrorCodes.PaymentMethodBindingFailed, 400);
        }
    }

    public async Task<Result> DetachPaymentMethodAsync(PaymentProviderResolveMethodDto input)
    {
        if (string.IsNullOrWhiteSpace(input.PaymentMethodToken))
            return Result.Failure(ErrorCodes.PaymentMethodNotFound, 400);

        try
        {
            await new global::Stripe.PaymentMethodService(GetClient()).DetachAsync(input.PaymentMethodToken);
            return Result.Success();
        }
        catch (StripeException ex) when (ex.HttpStatusCode == HttpStatusCode.NotFound)
        {
            // 渠道侧已不存在：解绑是幂等操作，本地照常清理
            _logger.LogInformation("Stripe payment method {Token} already detached.", input.PaymentMethodToken);
            return Result.Success();
        }
        catch (StripeException ex)
        {
            _logger.LogError(ex, "Stripe payment method detach failed. Token: {Token}", input.PaymentMethodToken);
            return Result.Failure(ErrorCodes.PaymentMethodBindingFailed, 400);
        }
    }

    public async Task<Result<PaymentProviderChargeResult>> ChargeOffSessionAsync(PaymentProviderChargeDto input)
    {
        if (string.IsNullOrWhiteSpace(input.PaymentMethodToken))
            return Result.Failure<PaymentProviderChargeResult>(ErrorCodes.SubscriptionPaymentMethodMissing, 400);

        try
        {
            var client = GetClient();
            var service = new PaymentIntentService(client);

            var options = new PaymentIntentCreateOptions
            {
                Amount = CurrencyInfo.ToMinorUnits(input.Amount, input.Currency),
                Currency = input.Currency.ToLowerInvariant(),
                Customer = input.ProviderCustomerId,
                PaymentMethod = input.PaymentMethodToken,
                // 无人值守扣款：立即确认 + 标记为非交互场景，触发已保存卡的链下扣款
                Confirm = true,
                OffSession = true,
                Description = input.Description,
                StatementDescriptorSuffix = StatementDescriptorSuffix,
                // 带上无人值守标记：这笔的 payment_failed 回调是终态（见 MapCallbackStatus）
                Metadata = BuildIntentMetadata(input.TradeNo, input.BusinessOrderNo, offSession: true, _options.Value)
            };

            // 幂等键按内部流水号：即便"扣款成功但本地状态机推进失败"后换轮重试，
            // 只要流水号未变，Stripe 返回的仍是同一笔 PaymentIntent，不会二次扣款。
            var paymentIntent = await service.CreateAsync(options, Idempotent("pi", input.TradeNo));

            _logger.LogInformation("Stripe off-session charge created. TradeNo: {TradeNo}, IntentId: {IntentId}, Status: {Status}",
                input.TradeNo, paymentIntent.Id, paymentIntent.Status);

            return Result.Success(new PaymentProviderChargeResult
            {
                TradeNo = input.TradeNo,
                ExternalTradeNo = paymentIntent.Id,
                Status = MapStripeStatus(paymentIntent.Status),
                PaidAmount = paymentIntent.AmountReceived > 0
                    ? CurrencyInfo.FromMinorUnits(paymentIntent.AmountReceived, input.Currency)
                    : input.Amount
            });
        }
        catch (StripeException ex) when ((int)ex.HttpStatusCode >= 500 || ex.HttpStatusCode == HttpStatusCode.Conflict)
        {
            // 5xx 是 Stripe 自己出了问题（官方说法：结果不确定，带同一幂等键重试）；409 是同一幂等键的请求还在处理。
            // 两者都说不准钱扣没扣：按结果未知回报，由回调或到期核实（按 TradeNo 元数据找回 intent）落定，
            // 而不是当成拒付让订阅换一个流水号（也就换了幂等键）再扣一笔。
            _logger.LogWarning(ex, "Stripe off-session charge {TradeNo} ended with HTTP {Status}; its outcome is unknown.",
                input.TradeNo, (int)ex.HttpStatusCode);
            return Result.Failure<PaymentProviderChargeResult>(ex.StripeError?.Message ?? ex.Message, 502);
        }
        catch (StripeException ex)
        {
            // 链下扣款被拒（如卡需要 3DS / 余额不足）属预期失败，记录原因供降级 PastDue
            _logger.LogWarning(ex, "Stripe off-session charge failed. TradeNo: {TradeNo}", input.TradeNo);
            return Result.Success(new PaymentProviderChargeResult
            {
                TradeNo = input.TradeNo,
                ExternalTradeNo = ex.StripeError?.PaymentIntent?.Id,
                Status = PaymentStatus.Failed,
                FailReason = ex.StripeError?.Message ?? ex.Message
            });
        }
    }

    /// <summary>
    /// <paramref name="customer"/> 是不是本系统为计费账户 <paramref name="account"/> 建的客户；是则返回 <see langword="null"/>，
    /// 否则返回一句写进日志的原因。
    /// </summary>
    /// <remarks>
    /// 判据是 <see cref="CreateCustomerAsync"/> 写下的 <c>Metadata["BillingAccount"]</c>（<c>{种类}:{Id}</c>）。
    /// 引入计费账户之前建的客户只带 <c>Metadata["UserId"]</c>：对个人账户照旧按它认（同一个用户的老客户继续可用）。
    /// ★ 缺失不等于放行：一个不是本系统建的（或已删除的）customer 上挂着的卡，本系统无从证明它属于调用者。
    /// ★ 配置了应用标记时，客户带着<b>另一个</b>应用的标记一律拒绝（同一个 Stripe 账户里两套安装都叫
    /// <c>Installation:default</c> 时，靠它分得清）；没带标记的老客户照旧按账户认。
    /// 与 PayPal 侧按 <c>merchant_customer_id</c> 的校验同形；纯函数，便于不触网断言。
    /// </remarks>
    internal static string? DescribeCustomerOwnershipViolation(Customer? customer, BillingAccountRef account, StripeOptions? options = null)
    {
        if (customer == null || customer.Deleted == true)
            return "the customer does not exist or has been deleted";

        var metadata = customer.Metadata ?? new Dictionary<string, string>();

        if (options?.HasMetadataStamp == true
            && metadata.TryGetValue(options.MetadataStampKey, out var stamp)
            && !string.Equals(stamp, options.MetadataStampValue, StringComparison.Ordinal))
            return "the customer belongs to another application sharing this Stripe account";

        if (metadata.TryGetValue(BillingAccountMetadataKey, out var owner) && !string.IsNullOrWhiteSpace(owner))
        {
            return string.Equals(owner, account.Key, StringComparison.OrdinalIgnoreCase)
                ? null
                : "the customer was created for a different billing account";
        }

        if (account.TryGetUserId(out var userId)
            && metadata.TryGetValue("UserId", out var legacyOwner) && !string.IsNullOrWhiteSpace(legacyOwner))
        {
            return string.Equals(legacyOwner, userId.ToString(), StringComparison.OrdinalIgnoreCase)
                ? null
                : "the customer was created for a different user";
        }

        return "the customer carries no BillingAccount metadata, so it was not created for this account by this system";
    }

    /// <summary>本渠道建的客户上标记「这个客户属于哪个计费账户」的元数据键。</summary>
    internal const string BillingAccountMetadataKey = "BillingAccount";

    private async Task<string> CreateCustomerAsync(StripeClient client, BillingAccountRef account, string? name, string? email)
    {
        var metadata = new Dictionary<string, string> { { BillingAccountMetadataKey, account.Key } };
        // 个人账户照旧写 UserId：排障时在 Stripe 后台一眼认得出是谁，也让按 UserId 认主的老代码路径继续成立
        if (account.TryGetUserId(out var userId))
            metadata["UserId"] = userId.ToString();

        // 幂等键与引入计费账户之前相同（个人账户仍是 cus:{userId}），两次并发的首次绑卡不会建出两个客户
        var idempotencyKey = account.TryGetUserId(out var uid) ? $"cus:{uid}" : $"cus:{account.Key}";

        var customer = await new CustomerService(client).CreateAsync(new CustomerCreateOptions
        {
            Name = string.IsNullOrWhiteSpace(name) ? null : name,
            Email = string.IsNullOrWhiteSpace(email) ? null : email,
            Metadata = WithAppStamp(metadata, _options.Value)
        }, new RequestOptions { IdempotencyKey = idempotencyKey });

        return customer.Id;
    }

    /// <summary>每笔卡支付的账单描述后缀；未配置时为 null（不设）。</summary>
    private string? StatementDescriptorSuffix
        => string.IsNullOrWhiteSpace(_options.Value.StatementDescriptorSuffix) ? null : _options.Value.StatementDescriptorSuffix;

    /// <summary>
    /// 把支付方式事件翻译成回调结果：<c>detached</c> = 凭据失效；<c>updated</c> / <c>automatically_updated</c> = 只刷新展示信息；
    /// 其余（<c>attached</c> 等）已接收、无需处理。
    /// </summary>
    internal static PaymentProviderCallbackResult MapPaymentMethodEvent(Event stripeEvent, StripePaymentMethod paymentMethod)
    {
        switch (stripeEvent.Type)
        {
            // 用户在 Stripe 侧移除了这张卡（或商户在后台删了它）。
            // 不处理的话，本地会一直拿着一个已经作废的凭据，直到下次续费扣款失败才发现。
            case "payment_method.detached":
                return new PaymentProviderCallbackResult
                {
                    EventId = stripeEvent.Id,
                    Kind = PaymentCallbackKind.PaymentMethodRevoked,
                    PaymentMethodToken = paymentMethod.Id
                };

            // 持卡人更新了有效期，或发卡行经 Stripe 的自动换卡（card account updater）换了卡号 / 有效期：
            // 凭据不变，只是本地展示的尾号与有效期过时了。不刷新，「卡快过期了」的提醒会对着一张早已换过的卡发。
            case "payment_method.updated":
            case "payment_method.automatically_updated":
                return new PaymentProviderCallbackResult
                {
                    EventId = stripeEvent.Id,
                    Kind = PaymentCallbackKind.PaymentMethodUpdated,
                    PaymentMethodToken = paymentMethod.Id,
                    UpdatedPaymentMethod = new PaymentProviderPaymentMethodResult
                    {
                        Token = paymentMethod.Id,
                        ProviderCustomerId = paymentMethod.CustomerId,
                        MethodType = MapStripeMethodType(paymentMethod),
                        Brand = paymentMethod.Card?.Brand,
                        Last4 = paymentMethod.Card?.Last4,
                        ExpiryMonth = paymentMethod.Card != null ? (int)paymentMethod.Card.ExpMonth : null,
                        ExpiryYear = paymentMethod.Card != null ? (int)paymentMethod.Card.ExpYear : null
                    }
                };

            // attached 同样带 PaymentMethod 载荷：把它当撤销会把用户刚绑好的卡立刻清掉
            default:
                return new PaymentProviderCallbackResult
                {
                    EventId = stripeEvent.Id,
                    Kind = PaymentCallbackKind.PaymentMethodRevoked,
                    PaymentMethodToken = paymentMethod.Id,
                    IsHandled = false
                };
        }
    }

    /// <summary>本模块写在 Stripe 退款元数据里的退款号的键。</summary>
    internal const string RefundNoMetadataKey = "RefundNo";

    /// <summary>
    /// <c>charge.refunded</c>：这笔收款上的全部退款。新版本 API 的事件里不再内嵌退款列表，缺了（或不全）就按 charge 去查；
    /// 查不到答 502 让 Stripe 重投，而不是当成「没有退款」吞掉。
    /// </summary>
    private async Task<Result<PaymentProviderCallbackResult>> MapChargeRefundedAsync(Event stripeEvent, global::Stripe.Charge charge)
    {
        var refunds = charge.Refunds?.Data;
        if (refunds == null || charge.Refunds!.HasMore)
        {
            try
            {
                var listed = await new global::Stripe.RefundService(GetClient()).ListAsync(new RefundListOptions { Charge = charge.Id, Limit = 100 });
                refunds = listed.Data;
            }
            catch (StripeException ex)
            {
                _logger.LogWarning(ex, "Stripe refunds for charge {ChargeId} could not be listed; the event will be redelivered.", charge.Id);
                return Result.Failure<PaymentProviderCallbackResult>(ErrorCodes.StripeRefundQueryFailed, 502);
            }
        }

        return Result.Success(MapRefundEvent(stripeEvent, refunds, charge.PaymentIntentId));
    }

    /// <summary>把渠道退款翻译成回调结果。带本模块退款号的那几笔标上 <see cref="PaymentProviderExternalRefund.LocalRefundNo"/>。</summary>
    internal static PaymentProviderCallbackResult MapRefundEvent(Event stripeEvent, IEnumerable<global::Stripe.Refund> refunds, string? paymentIntentId)
    {
        return new PaymentProviderCallbackResult
        {
            EventId = stripeEvent.Id,
            Kind = PaymentCallbackKind.ExternalRefund,
            ExternalTradeNo = paymentIntentId,
            Refunds = refunds
                .Where(r => !string.IsNullOrWhiteSpace(r.Id))
                .Select(r => new PaymentProviderExternalRefund
                {
                    ExternalRefundNo = r.Id,
                    LocalRefundNo = r.Metadata != null && r.Metadata.TryGetValue(RefundNoMetadataKey, out var refundNo) && !string.IsNullOrWhiteSpace(refundNo)
                        ? refundNo
                        : null,
                    Amount = CurrencyInfo.FromMinorUnits(r.Amount, r.Currency),
                    Currency = r.Currency?.ToUpperInvariant(),
                    Status = MapStripeRefundStatus(r.Status),
                    Reason = r.Reason
                })
                .ToList()
        };
    }

    /// <summary>把 Stripe 争议翻译成回调结果。</summary>
    internal static PaymentProviderCallbackResult MapDisputeEvent(Event stripeEvent, global::Stripe.Dispute dispute)
        => new()
        {
            EventId = stripeEvent.Id,
            Kind = PaymentCallbackKind.Dispute,
            ExternalTradeNo = dispute.PaymentIntentId,
            Dispute = new PaymentProviderDispute
            {
                DisputeId = dispute.Id,
                Status = MapDisputeStatus(dispute.Status),
                Reason = dispute.Reason,
                Amount = CurrencyInfo.FromMinorUnits(dispute.Amount, dispute.Currency),
                Currency = dispute.Currency?.ToUpperInvariant()
            }
        };

    internal static PaymentDisputeStatus MapDisputeStatus(string? status) => status switch
    {
        "warning_needs_response" => PaymentDisputeStatus.WarningNeedsResponse,
        "warning_under_review" => PaymentDisputeStatus.WarningUnderReview,
        "warning_closed" => PaymentDisputeStatus.WarningClosed,
        "needs_response" => PaymentDisputeStatus.NeedsResponse,
        "under_review" => PaymentDisputeStatus.UnderReview,
        "won" => PaymentDisputeStatus.Won,
        "lost" => PaymentDisputeStatus.Lost,
        _ => PaymentDisputeStatus.Unknown
    };

    /// <summary>
    /// 这笔已收款的 intent 按要求留存下来的支付方式（建单时带了 <c>setup_future_usage</c>）；其余情况 null。
    /// </summary>
    internal static string? SavedPaymentMethodOf(PaymentIntent intent)
        => string.Equals(intent.Status, "succeeded", StringComparison.Ordinal)
            && !string.IsNullOrWhiteSpace(intent.SetupFutureUsage)
            && !string.IsNullOrWhiteSpace(intent.PaymentMethodId)
                ? intent.PaymentMethodId
                : null;

    private static PaymentProviderQueryResult BuildQueryResult(PaymentIntent intent, string fallbackTradeNo)
    {
        return new PaymentProviderQueryResult
        {
            TradeNo = intent.Metadata != null && intent.Metadata.TryGetValue("TradeNo", out var internalNo)
                ? internalNo
                : fallbackTradeNo,
            ExternalTradeNo = intent.Id,
            Status = MapStripeStatus(intent.Status),
            Amount = CurrencyInfo.FromMinorUnits(intent.Amount, intent.Currency),
            PaidTime = intent.Status == "succeeded" ? DateTime.UtcNow : null,
            FailReason = intent.LastPaymentError?.Message,
            SavedPaymentMethodToken = SavedPaymentMethodOf(intent)
        };
    }

    /// <summary>
    /// Stripe search 查询语法用单引号包裹字符串值，值内单引号需转义，避免注入到查询表达式
    /// </summary>
    private static string EscapeSearchValue(string value) => value.Replace("'", "\\'", StringComparison.Ordinal);

    /// <summary>按内部流水号搜 PaymentIntent 的查询；配置了应用标记时只搜本应用建的。</summary>
    internal static string BuildTradeNoSearchQuery(string tradeNo, StripeOptions options)
    {
        var query = $"metadata['TradeNo']:'{EscapeSearchValue(tradeNo)}'";
        return options.HasMetadataStamp
            ? $"{query} AND metadata['{EscapeSearchValue(options.MetadataStampKey)}']:'{EscapeSearchValue(options.MetadataStampValue!)}'"
            : query;
    }

    /// <summary>配置了应用标记时把它加进元数据；否则原样返回。</summary>
    internal static Dictionary<string, string> WithAppStamp(Dictionary<string, string> metadata, StripeOptions options)
    {
        if (options.HasMetadataStamp)
            metadata[options.MetadataStampKey] = options.MetadataStampValue!;
        return metadata;
    }

    /// <summary>
    /// 这个对象是不是本应用建的：没配置应用标记时一律是（与引入标记之前逐字相同）；配置了就要求元数据里带着同一个标记。
    /// </summary>
    internal static bool CarriesAppStamp(IDictionary<string, string>? metadata, StripeOptions options)
        => !options.HasMetadataStamp
            || (metadata != null
                && metadata.TryGetValue(options.MetadataStampKey, out var value)
                && string.Equals(value, options.MetadataStampValue, StringComparison.Ordinal));

    private static PaymentMethodEnum MapStripeMethodType(StripePaymentMethod paymentMethod)
    {
        var walletType = paymentMethod.Card?.Wallet?.Type;
        if (string.Equals(walletType, "apple_pay", StringComparison.OrdinalIgnoreCase))
            return PaymentMethodEnum.ApplePay;
        if (string.Equals(walletType, "google_pay", StringComparison.OrdinalIgnoreCase))
            return PaymentMethodEnum.GooglePay;

        return paymentMethod.Type switch
        {
            "card" => string.Equals(paymentMethod.Card?.Funding, "debit", StringComparison.OrdinalIgnoreCase)
                ? PaymentMethodEnum.DebitCard
                : PaymentMethodEnum.CreditCard,
            "paypal" => PaymentMethodEnum.PayPal,
            _ => PaymentMethodEnum.CreditCard
        };
    }

    /// <summary>
    /// Webhook 事件的支付状态：先看**事件类型**，再回落到 intent 状态。
    /// </summary>
    /// <remarks>
    /// Stripe 的 PaymentIntent <b>没有 failed 这个状态</b> —— 一笔扣款失败之后，
    /// intent 被退回 <c>requires_payment_method</c> 好让客户换一张卡重试。
    /// 于是只看 intent 状态时，<c>payment_intent.payment_failed</c> 读出来是「处理中」：
    /// 本地状态机拿不到终态，<c>PaymentFailedEvent</c> 永不发布，订阅不降级 PastDue，
    /// 为这笔订单核销掉的优惠券要等过期扫描才归还。
    /// 而 <c>requires_payment_method</c> 同时也是**新建 intent 的正常初始状态**，
    /// 所以判据只能是事件类型，不能是 intent 状态 —— 与本文件里
    /// <c>payment_method.detached</c> 的处理同形。
    /// <para>
    /// ★ <c>payment_intent.payment_failed</c> 只对<b>无人值守扣款</b>（续费 / 试用转正，intent 带
    /// <see cref="OffSessionMetadataKey"/> 标记）是终态：没有人会在收银台前换卡，intent 退回
    /// <c>requires_payment_method</c> 之后不会再有尝试。收银台上的一次拒付则不是：intent 退回
    /// <c>requires_payment_method</c> 正是为了让客户在同一个 Payment Element 里换一张卡重试。
    /// 此前一律判成 Failed，本地支付当场终结（券归还、发失败事件、业务单取消），客户换卡付掉之后的成功回调
    /// 撞上本地终态被吞掉，钱收了，订单没履约。所以那种拒付读作处理中：本地支付等成功回调，
    /// 或等过期扫描作废 intent 后过期（那时才归还券、发过期事件）。
    /// </para>
    /// </remarks>
    private static PaymentStatus MapCallbackStatus(string? eventType, PaymentIntent intent)
    {
        return eventType switch
        {
            "payment_intent.payment_failed" => IsOffSessionIntent(intent) ? PaymentStatus.Failed : PaymentStatus.Processing,
            "payment_intent.canceled" => PaymentStatus.Cancelled,
            _ => MapStripeStatus(intent.Status)
        };
    }

    /// <summary>
    /// intent 元数据里标记「这是一笔无人值守扣款」的键。只由 <see cref="ChargeOffSessionAsync"/> 写入。
    /// </summary>
    internal const string OffSessionMetadataKey = "OffSession";

    /// <summary>
    /// 本系统建的 PaymentIntent 统一带的元数据：内部流水号（回调凭它找回本地支付）、业务单号，
    /// 无人值守扣款另带 <see cref="OffSessionMetadataKey"/>。
    /// </summary>
    internal static Dictionary<string, string> BuildIntentMetadata(string tradeNo, string businessOrderNo, bool offSession, StripeOptions? options = null)
    {
        var metadata = new Dictionary<string, string>
        {
            { "TradeNo", tradeNo },
            { "BusinessOrderNo", businessOrderNo }
        };

        if (offSession)
            metadata[OffSessionMetadataKey] = "true";

        return options == null ? metadata : WithAppStamp(metadata, options);
    }

    private static bool IsOffSessionIntent(PaymentIntent intent)
        => intent.Metadata != null
            && intent.Metadata.TryGetValue(OffSessionMetadataKey, out var flag)
            && string.Equals(flag, "true", StringComparison.OrdinalIgnoreCase);

    private static PaymentStatus MapStripeStatus(string? status)
    {
        return status switch
        {
            "requires_payment_method" or "requires_confirmation" or "requires_action" => PaymentStatus.Processing,
            "succeeded" => PaymentStatus.Succeeded,
            "canceled" => PaymentStatus.Cancelled,
            _ => PaymentStatus.Processing
        };
    }

    /// <summary>
    /// Stripe 退款状态映射。
    /// pending 必须落成 Refunding 而不是 Succeeded：银行卡退回常需数日，
    /// 过早记成功会让账面与渠道脱节，还会连带把支付回写成已退款。
    /// </summary>
    private static RefundStatus MapStripeRefundStatus(string? status)
    {
        return status switch
        {
            "succeeded" => RefundStatus.Succeeded,
            "failed" => RefundStatus.Failed,
            "canceled" => RefundStatus.Cancelled,
            _ => RefundStatus.Refunding
        };
    }
}
