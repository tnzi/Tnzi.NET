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

            var options = new PaymentIntentCreateOptions
            {
                Amount = CurrencyInfo.ToMinorUnits(input.Amount, input.Currency),
                Currency = input.Currency.ToLowerInvariant(),
                Customer = input.ProviderCustomerId,
                Metadata = new Dictionary<string, string>
                {
                    { "TradeNo", input.TradeNo },
                    { "BusinessOrderNo", input.BusinessOrderNo }
                },
                AutomaticPaymentMethods = new PaymentIntentAutomaticPaymentMethodsOptions
                {
                    Enabled = true
                }
            };

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

    public async Task<Result<PaymentProviderQueryResult>> QueryPaymentAsync(string tradeNo)
    {
        try
        {
            var client = GetClient();
            var service = new PaymentIntentService(client);

            var searchResult = await service.SearchAsync(new PaymentIntentSearchOptions
            {
                Query = $"metadata['TradeNo']:'{EscapeSearchValue(tradeNo)}'"
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
                Reason = RefundReasons.RequestedByCustomer
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
            var stripeEvent = EventUtility.ParseEvent(rawBody);

            // 用户在 Stripe 侧移除了这张卡（或商户在后台删了它）。
            // 不处理的话，本地会一直拿着一个已经作废的凭据，直到下次续费扣款失败才发现。
            if (stripeEvent.Data.Object is StripePaymentMethod detachedMethod)
            {
                _logger.LogInformation("Stripe payment method {Token} was detached at the channel.", detachedMethod.Id);

                return Task.FromResult(Result.Success(new PaymentProviderCallbackResult
                {
                    EventId = stripeEvent.Id,
                    Kind = PaymentCallbackKind.PaymentMethodRevoked,
                    PaymentMethodToken = detachedMethod.Id,
                    // 只有"已解绑"才代表凭据失效；attached / updated 同样带 PaymentMethod 载荷，
                    // 把它们一并当撤销会把用户刚绑好的卡立刻清掉
                    IsHandled = string.Equals(stripeEvent.Type, "payment_method.detached", StringComparison.Ordinal)
                }));
            }

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
                Status = MapCallbackStatus(stripeEvent.Type, paymentIntent.Status),
                // 金额已按这个币种从最小单位换算回来；不把它带上，服务层就只能比数值
                Currency = paymentIntent.Currency,
                PaidAmount = paymentIntent.AmountReceived > 0
                    ? CurrencyInfo.FromMinorUnits(paymentIntent.AmountReceived, paymentIntent.Currency)
                    : CurrencyInfo.FromMinorUnits(paymentIntent.Amount, paymentIntent.Currency),
                FailReason = paymentIntent.LastPaymentError?.Message,
                EventId = stripeEvent.Id
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

        try
        {
            _ = EventUtility.ConstructEvent(rawBody, signature, _options.Value.WebhookSecret);
            return Task.FromResult(true);
        }
        catch (StripeException ex)
        {
            _logger.LogWarning(ex, "Stripe webhook signature verification failed.");
            return Task.FromResult(false);
        }
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
        return await QueryPaymentAsync(tradeNo);
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
                    Query = $"metadata['TradeNo']:'{EscapeSearchValue(tradeNo)}'"
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

            if (string.IsNullOrWhiteSpace(customerId))
            {
                customerId = await CreateCustomerAsync(client, input.UserId, input.CustomerName, input.CustomerEmail);
            }

            var setupIntent = await new SetupIntentService(client).CreateAsync(new SetupIntentCreateOptions
            {
                Customer = customerId,
                // off_session：声明该支付方式将用于后续无人值守扣款，Stripe 会据此在收集阶段完成必要的授权
                Usage = "off_session",
                PaymentMethodTypes = ["card"],
                Metadata = new Dictionary<string, string>
                {
                    { "UserId", input.UserId.ToString() }
                }
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
                var violation = DescribeCustomerOwnershipViolation(customer, input.UserId);
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
                customerId = await CreateCustomerAsync(client, input.UserId, input.CustomerName, input.CustomerEmail);
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
                Metadata = new Dictionary<string, string>
                {
                    { "TradeNo", input.TradeNo },
                    { "BusinessOrderNo", input.BusinessOrderNo }
                }
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
    /// <paramref name="customer"/> 是不是本系统为 <paramref name="userId"/> 建的客户；是则返回 <see langword="null"/>，
    /// 否则返回一句写进日志的原因。
    /// </summary>
    /// <remarks>
    /// 判据是 <see cref="CreateCustomerAsync"/> 写下的 <c>Metadata["UserId"]</c>。★ 缺失不等于放行：
    /// 一个不是本系统建的（或已删除的）customer 上挂着的卡，本系统无从证明它属于调用者。
    /// 与 PayPal 侧按 <c>merchant_customer_id</c> 的校验同形；纯函数，便于不触网断言。
    /// </remarks>
    internal static string? DescribeCustomerOwnershipViolation(Customer? customer, Guid userId)
    {
        if (customer == null || customer.Deleted == true)
            return "the customer does not exist or has been deleted";

        if (customer.Metadata == null || !customer.Metadata.TryGetValue("UserId", out var owner) || string.IsNullOrWhiteSpace(owner))
            return "the customer carries no UserId metadata, so it was not created for this user by this system";

        return string.Equals(owner, userId.ToString(), StringComparison.OrdinalIgnoreCase)
            ? null
            : "the customer was created for a different user";
    }

    private static async Task<string> CreateCustomerAsync(StripeClient client, Guid userId, string? name, string? email)
    {
        var customer = await new CustomerService(client).CreateAsync(new CustomerCreateOptions
        {
            Name = string.IsNullOrWhiteSpace(name) ? null : name,
            Email = string.IsNullOrWhiteSpace(email) ? null : email,
            Metadata = new Dictionary<string, string>
            {
                { "UserId", userId.ToString() }
            }
        }, new RequestOptions { IdempotencyKey = $"cus:{userId}" });

        return customer.Id;
    }

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
            FailReason = intent.LastPaymentError?.Message
        };
    }

    /// <summary>
    /// Stripe search 查询语法用单引号包裹字符串值，值内单引号需转义，避免注入到查询表达式
    /// </summary>
    private static string EscapeSearchValue(string value) => value.Replace("'", "\\'", StringComparison.Ordinal);

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
    /// </remarks>
    private static PaymentStatus MapCallbackStatus(string? eventType, string? intentStatus)
    {
        return eventType switch
        {
            "payment_intent.payment_failed" => PaymentStatus.Failed,
            "payment_intent.canceled" => PaymentStatus.Cancelled,
            _ => MapStripeStatus(intentStatus)
        };
    }

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
