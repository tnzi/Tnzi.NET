using Tnzi.Documents.Signing.Entities;
using Tnzi.Documents.Signing.Metadata;
using Tnzi.Documents.Signing.Services.Internal;

namespace Tnzi.Documents.Signing.Tests;

/// <summary>
/// 模板快照。
/// </summary>
/// <remarks>
/// 权威内容是这份快照而不是活的模板：中途有人改了模板，正在看这份文件的签署人不该因此
/// 看到不同的东西，已经收集到的签名更不该因为框挪了位置而失去意义。
/// </remarks>
public class SigningSnapshotTests
{
    private static Field Field(string key, decimal x = 0.1m, int page = 1) => new()
    {
        Key = key,
        Label = key,
        Type = SigningFieldType.Text,
        RecipientRole = "Client",
        Required = true,
        PlacementMode = FieldPlacementMode.Absolute,
        Page = page,
        X = x,
        Y = 0.2m,
        W = 0.3m,
        H = 0.05m,
        FontSize = 11m,
    };

    [Fact]
    public void A_snapshot_round_trips_every_placement_detail()
    {
        // 落点、页码、字号全都要活过序列化 —— 它们决定签名盖在哪。
        var snapshot = new SigningSnapshot
        {
            TemplateId = Guid.NewGuid(),
            TemplateVersion = 3,
            TemplateName = "Retainer",
            Fields = [SnapshotField.From(Field("clientName", 0.15m, page: 2))],
        };

        var restored = SigningSnapshot.FromJson(snapshot.ToJson());

        restored.ShouldNotBeNull();
        restored!.TemplateVersion.ShouldBe(3);
        restored.Fields.Count.ShouldBe(1);
        var f = restored.Fields[0];
        f.Key.ShouldBe("clientName");
        f.Page.ShouldBe(2);
        f.X.ShouldBe(0.15m);
        f.H.ShouldBe(0.05m);
        f.FontSize.ShouldBe(11m);
        f.RecipientRole.ShouldBe("Client");
        f.Required.ShouldBeTrue();
    }

    [Fact]
    public void A_snapshot_is_decoupled_from_the_live_template()
    {
        // 拍完快照后改模板字段，快照必须纹丝不动 —— 这正是它存在的理由。
        var field = Field("amount");
        var snapshot = new SigningSnapshot { Fields = [SnapshotField.From(field)] };
        var json = snapshot.ToJson();

        field.X = 0.9m;
        field.Label = "CHANGED";

        var restored = SigningSnapshot.FromJson(json)!;
        restored.Fields[0].X.ShouldBe(0.1m);
        restored.Fields[0].Label.ShouldBe("amount");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{ not json")]
    [InlineData("[]")]
    public void An_unreadable_snapshot_resolves_to_null_not_to_an_empty_one(string? json)
    {
        // ★ 关键区别。返回一份"没有字段的快照"会让密封安静地产出一张白纸；
        //   返回 null 强迫调用方把它当作"这份请求无法处理"。
        SigningSnapshot.FromJson(json).ShouldBeNull();
    }

    [Fact]
    public void Signature_like_fields_are_recognised_by_type()
    {
        // 密封时签名字段走图片盖章、其余走文本盖章，分流全靠这个判据。
        SnapshotField.From(Field("a")).IsSignatureLike.ShouldBeFalse();

        var sig = new SnapshotField { Key = "s", Type = SigningFieldType.Signature };
        var ini = new SnapshotField { Key = "i", Type = SigningFieldType.Initials };
        sig.IsSignatureLike.ShouldBeTrue();
        ini.IsSignatureLike.ShouldBeTrue();
    }
}
