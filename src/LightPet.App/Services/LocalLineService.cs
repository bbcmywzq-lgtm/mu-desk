using LightPet.Core.Behavior;

namespace LightPet.App.Services;

internal sealed class LocalLineService
{
    private const int RecentLineLimit = 6;
    private static readonly IReadOnlyDictionary<string, string[]> Lines =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["touch-head"] =
            [
                "嗯……手很暖。", "今天也辛苦啦。", "再摸一下也可以。", "头发没有乱吧？",
                "这里摸起来很舒服。", "我有认真待在这里。", "好啦，我知道你在。", "轻轻的就好。",
                "刚才在想事情，被你打断了。", "这样会让人放松下来。", "再停一会儿也没关系。", "你的手比刚才暖一点。",
            ],
            ["touch-body"] =
            [
                "我在这里。", "怎么啦？", "要一起休息一下吗？", "是在确认我还在吗？",
                "有事可以慢慢说。", "我没有走神……大概。", "今天进行得还顺利吗？", "你碰到我的衣角了。",
                "我会陪着你的。", "先别急，想清楚再继续。", "需要我帮你记点什么吗？", "休息一下也算正事。",
            ],
            ["cheek-poke"] =
            [
                "欸。", "脸会被戳扁的。", "看到你了。", "为什么偏偏戳这里？",
                "一下就够了吧。", "有点痒。", "我可不是按钮。", "你是不是很闲呀？",
            ],
            ["annoyed-dodge"] =
            [
                "别一直戳嘛。", "我要躲开了。", "再戳就不理你了。", "这已经不是打招呼了。",
                "我记住次数了。", "到此为止。", "让我安静三秒。", "你赢了，我换个位置。",
            ],
            ["pinch"] =
            [
                "轻一点嘛。", "我没有那么小只！", "抓住我啦？", "衣服要被拎皱了。",
                "先说要把我放到哪里。", "这样悬着很奇怪。", "我自己会走的。", "至少抓稳一点。",
            ],
            ["wave"] =
            [
                "嗨。", "我在这边。", "看到你啦。", "路过也可以打个招呼。",
                "今天见过好几次了。", "忙你的，我只是挥挥手。", "又碰面啦。", "嗯，我注意到你了。",
            ],
            ["happy"] =
            [
                "好耶。", "心情变好了。", "这件事值得开心一下。", "顺利完成。",
                "看起来不错。", "今天也有好事情。", "可以小小庆祝一下。", "我就知道能办到。",
            ],
            ["sad"] =
            [
                "好像没有成功。", "没关系，再试一次。", "这次有点遗憾。", "先看看哪里出了问题。",
                "我没有接住。", "别急，东西还在原处。", "暂时行不通。", "我们换个办法吧。",
            ],
            ["surprised"] =
            [
                "欸？", "刚刚发生了什么？", "吓我一跳。", "这是要交给我吗？",
                "突然出现了。", "我看到了。", "等一下，让我接稳。", "原来还有这一招。",
            ],
            ["think"] =
            [
                "让我想一想。", "这件事可以换个顺序。", "还差一点线索。", "先抓住最重要的部分。",
                "复杂的事也能一点点拆开。", "我在整理思路。", "也许不用一次做完。", "先从最小的一步开始。",
            ],
            ["stretch"] =
            [
                "坐久了要活动一下。", "肩膀终于松开了。", "伸个懒腰再继续。", "别一直保持同一个姿势。",
                "休息半分钟也有效。", "呼……精神一点了。", "起来走两步吧。", "身体也需要保存进度。",
            ],
            ["hair-fix"] =
            [
                "头发又跑到前面了。", "发卡还在。", "整理好再继续。", "刚才是不是有风？",
                "这样看起来精神一点。", "紫色这边没有弄乱吧？", "等我整理一下。", "好了。",
            ],
            ["yawn"] =
            [
                "有一点困了。", "哈欠会传染吗？", "眼睛需要休息。", "今天好像过得很快。",
                "再撑一会儿也不会更高效。", "困意追上来了。", "该停一下了。", "我只是……稍微闭一下眼。",
            ],
            ["sit-rest"] =
            [
                "我先坐一会儿。", "这里刚好可以休息。", "不赶时间的话，就慢一点。", "安静待着也很好。",
                "脑子空一会儿。", "等准备好了再起来。", "今天已经做了不少。", "让我陪你歇一会儿。",
            ],
            ["sleep"] =
            [
                "我先睡一小会儿。", "晚一点再叫我。", "今天就到这里也可以。", "灯光好像变柔和了。",
                "别把休息留到最后。", "晚安……如果现在已经很晚。", "我会安静一点。", "明天再继续。",
            ],
            ["observe-cursor"] =
            [
                "你的鼠标在找什么？", "刚才从我旁边经过了。", "我看到光标了。", "是在犹豫点哪个吗？",
                "它又绕回来了。", "你移动得好快。", "我没有挡住吧？", "好像有什么东西要发生。",
            ],
            ["head-rub"] =
            [
                "慢一点就很好。", "这样很容易让人走神。", "嗯……继续也可以。", "头发真的要乱了。",
                "我暂时不动。", "有点像在安慰我。", "好吧，再一会儿。", "记得顺着头发。",
            ],
            ["say"] =
            [
                "今天也慢慢来。", "别忘了喝水。", "我会安静陪着你的。", "忙完记得伸个懒腰。",
                "先做眼前这一件就好。", "不确定的时候，可以先留下一个便签。", "完成比一次做到完美更重要。", "桌面有点忙，我尽量不挡路。",
                "如果卡住了，就把问题写成一句话。", "别把所有事情同时放在脑子里。", "偶尔停下来看看已经完成了多少。", "我不催你，只负责提醒你别太累。",
            ],
            ["say-workday"] =
            [
                "工作日也不用一直绷着。", "先处理最影响后续的那件事。", "消息很多的时候，先别全部回应。", "给今天留一个明确的结束点。",
                "重要的事情最好不要只记在脑子里。", "忙碌不等于事情正在向前。", "要不要先清掉一个很小的任务？", "别让临时事情挤走真正重要的事。",
            ],
            ["say-restday"] =
            [
                "今天可以不用那么有计划。", "休息日就别把自己排得太满。", "做一点喜欢的事吧。", "发呆也算合理安排。",
                "如果不赶时间，就绕远一点。", "今天适合慢慢收拾桌面。", "不要偷偷把休息日变成补班。", "留一点没有用途的时间。",
            ],
            ["say-morning"] =
            [
                "早上好，先让自己醒过来。", "新的一天不用一开始就冲刺。", "先喝点水，再决定第一件事。", "早晨适合做需要清醒思路的事。",
                "窗外现在亮起来了吗？", "先完成一个小开头。", "今天想保住哪一件最重要的事？", "别忘了吃点东西。",
            ],
            ["say-work-morning"] =
            [
                "上午的注意力很珍贵。", "趁思路还清楚，先做难的部分。", "别让通知把上午切得太碎。", "先留一段不被打断的时间。",
                "现在适合把框架搭起来。", "如果要开很多窗口，记得留一个主线。", "先确定做到什么算完成。", "上午还长，不用着急。",
            ],
            ["say-midday"] =
            [
                "到中午了，先吃饭吧。", "午间不适合硬扛。", "离开屏幕看一会儿远处。", "下午的精神要靠现在补回来。",
                "吃饭的时候就别盯着工作了。", "坐久了，起来走走。", "给脑子一点空白。", "午休十分钟也比没有好。",
            ],
            ["say-afternoon"] =
            [
                "下午容易走神，任务拆小一点。", "困的话先动一动。", "现在适合检查，而不是盲目加速。", "剩下的时间够做好一件事。",
                "别被快下班的感觉骗着乱做。", "先把手上的事情收一个口。", "下午也要记得喝水。", "如果效率下降，就换一种任务。",
            ],
            ["say-evening"] =
            [
                "天色晚了，开始收尾吧。", "晚上适合整理，不适合无限开新坑。", "把明天第一步写下来，就可以放心停。", "今天没做完的，不代表今天白过。",
                "给今天留一点自己的时间。", "屏幕亮度可以调低一些。", "已经辛苦一天了。", "晚饭吃了吗？",
            ],
            ["say-late-night"] =
            [
                "已经很晚了。", "明天清醒的时候会更容易。", "先保存，再休息。", "夜里做决定容易把事情想得太重。",
                "最后一件，做完就停。", "眼睛已经在抗议了。", "不必靠熬夜证明认真。", "我陪你收尾，然后去睡吧。",
            ],
            ["shelf-added"] =
            [
                "接住了，已放好 {0} 项。", "已经替你搁下 {0} 项。", "{0} 项已放进 Drop。", "收到了，原文件还在原处。",
                "放好了，需要时再从货架拖走。", "我接稳了，货架里多了 {0} 项。",
            ],
        };

    private readonly Queue<string> _recentLines = new();
    private readonly Random _random = new();

    public string Pick(string context) => PickFromContexts(context);

    public string PickContextual(PetContextSnapshot context)
    {
        var dayContext = context.DayType == PetDayType.RestDay
            ? "say-restday"
            : "say-workday";
        var timeContext = context.TimeBlock switch
        {
            PetTimeBlock.Morning => "say-morning",
            PetTimeBlock.WorkMorning => "say-work-morning",
            PetTimeBlock.Midday => "say-midday",
            PetTimeBlock.Afternoon => "say-afternoon",
            PetTimeBlock.Evening => "say-evening",
            _ => "say-late-night",
        };
        return PickFromContexts(timeContext, dayContext, "say");
    }

    public string PickShelfAdded(int addedCount) => string.Format(
        System.Globalization.CultureInfo.CurrentCulture,
        PickFromContexts("shelf-added"),
        Math.Max(0, addedCount));

    private string PickFromContexts(params string[] contexts)
    {
        var choices = contexts
            .Where(Lines.ContainsKey)
            .SelectMany(context => Lines[context])
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (choices.Length == 0)
        {
            choices = ["我在这里。"];
        }

        var fresh = choices.Where(line => !_recentLines.Contains(line)).ToArray();
        var pool = fresh.Length > 0 ? fresh : choices;
        var selected = pool[_random.Next(pool.Length)];
        _recentLines.Enqueue(selected);
        while (_recentLines.Count > RecentLineLimit)
        {
            _recentLines.Dequeue();
        }

        return selected;
    }
}
