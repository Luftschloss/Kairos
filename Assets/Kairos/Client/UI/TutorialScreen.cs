using System;
using Kairos.Engine;
using UnityEngine;
using UnityEngine.UI;

namespace Kairos.Client
{
    public sealed partial class GameController
    {
        const string TutorialPreference = "Kairos.Tutorial.Completed.v1";
        TutorialLesson lesson;
        bool tutorialIntro;
        int helpPage;
        public bool TutorialCompleted => progression != null && progression.Data.Profile.Entitlements.Contains(Kairos.Progression.ProgressionService.TutorialGrant);
        public bool IsTutorial => lesson != null;
        public TutorialStep LessonStep => lesson == null ? TutorialStep.Deploy : lesson.Step;
        bool LessonAllows(Command c) => lesson == null || !tutorialIntro && lesson.Allows(c);

        void RequestMode(Action start)
        {
            if (TutorialCompleted) start(); else ShowTutorialWelcome(start);
        }
        void ShowTutorialWelcome(Action skip)
        {
            var box = Modal("第一次来到刻律之城？");
            Label(box, new Rect(40, 115, 1050, 64), "先玩一场约 2 分钟的教学残局。无需记住全部规则。", 26, ink);
            Label(box, new Rect(40, 213, 1050, 155), "你会亲手完成：出牌 → 攻击 → 鸣钟 → 击败训练钟偶。\n每一步都有目标与高亮；无需应付倒计时或 AI 催促。\n学完后，再挑战本地 PVP 或 AI。教学可以随时退出、重玩。", 24, muted);
            Button(box, new Rect(40, 459, 610, 77), "开始新手教学（推荐）", StartTutorial, true, new Color32(30, 88, 85, 255), 27);
            Button(box, new Rect(690, 459, 410, 77), skip == null ? "暂时跳过，返回菜单" : "跳过，直接进入对局", () => { if (skip == null) ShowMenu(); else skip(); }, true, background, 21);
            Label(box, new Rect(40, 557, 1050, 32), "仅完成教学后才会记录“已学会”；跳过不会标记通关。", 17, muted);
        }
        public void StartTutorial()
        {
            retrySave = null;
            lesson = TutorialLesson.Create(Database); State = lesson.State; Run = null; Mode = DemoMode.Practice; viewer = 0;
            tutorialIntro = true; BeginMatch();
        }
        void DrawTutorialIntro()
        {
            var box = Modal("新手教学 · 把时间交还给你");
            Label(box, new Rect(40, 108, 1040, 62), "目标：把训练钟偶的 4 点命烛降到 0。", 29, teal);
            Label(box, new Rect(40, 203, 1050, 190), "命烛就是生命。你在下方，对手在上方。\n本作没有“结束回合”：谁的时标较小，谁行动。\n卡牌左上角的“刻”就是费用；先发生效果，再向前移动时标。\n跟着右侧教学目标，点击金色高亮的牌、目标和按钮。", 24, ink);
            Label(box, new Rect(40, 415, 1050, 65), "这是固定教学残局：跳过调度，钟偶在第 2 刻、4 命烛且逆流已用。\n普通对局双方从第 0 刻与 20 命烛开始；这里的出牌和战斗仍使用相同规则。", 18, muted);
            Button(box, new Rect(40, 520, 320, 62), "暂时退出", ShowMenu, true, background, 21);
            Button(box, new Rect(730, 520, 370, 62), "开始第一步", () => { tutorialIntro = false; RebuildBattle(); }, true, new Color32(30, 88, 85, 255), 25);
        }
        string TutorialTitle()
        {
            switch (lesson.Step)
            {
                case TutorialStep.Deploy: return "1 / 5   打出你的第一张牌";
                case TutorialStep.FirstAttack: return "2 / 5   用侍从攻击钟偶";
                case TutorialStep.Chime: return "3 / 5   平局后连续行动";
                case TutorialStep.OpponentWait: return "4 / 5   观察对手行动";
                default: return "5 / 5   再次攻击，取得胜利";
            }
        }
        void DrawTutorialSidebar()
        {
            var side = Box(root, new Rect(1210, 90, 358, 783), panel);
            Label(side, new Rect(18, 16, 322, 40), "新手教学 · 无倒计时", 23, gold);
            for (int i = 0; i < 5; i++) Box(side, new Rect(18 + i * 65, 66, 57, 5), i <= (int)lesson.Step ? teal : background);
            Label(side, new Rect(18, 89, 322, 71), TutorialTitle(), 24, gold);
            string explanation, instruction;
            switch (lesson.Step)
            {
                case TutorialStep.Deploy:
                    explanation = "你在 0 刻，钟偶在 2 刻。\n你落后，所以由你行动。\n\n晨光斥候耗时 1 刻。\n另一张牌要 3 刻，超过开局视界 2，暂时不能出。";
                    instruction = selectedCard == 0 ? "将下方「晨光斥候」拖到己方部署区；也可点击选牌。" : "看中央 0 → 1 刻预览。\n拖入部署区，或点「确认行动」。"; break;
                case TutorialStep.FirstAttack:
                    explanation = "出牌后，你在 1 刻，仍比钟偶的 2 刻落后，所以还是你行动。\n\n斥候有「迅捷」：入场即可攻击。2 攻 / 1 血 / 节奏 1，表示攻击造成 2 伤害、花 1 刻。";
                    instruction = selectedUnit == 0 ? "从己方斥候拖到上方钟偶；也可点击斥候。" : selectedTarget.Kind == TargetKind.None ? "拖到 / 点击钟偶命烛面板。\n先看预计到 2 刻，仍由你行动。" : "目标已选好。点「确认行动」。"; break;
                case TutorialStep.Chime:
                    explanation = "钟偶命烛 4 → 2，斥候变为疲惫。\n双方都在 2 刻，但你后到，所以仍然由你行动。\n\n蓄势花 2 刻抽 1 张，每小节限一次。走到第 4 刻还会鸣钟。";
                    instruction = selectedAction == CommandKind.Focus ? "中央显示预计鸣钟 1 次。\n点「确认行动」，观察抽牌与重整。" : "点「蓄势预览」，先看 2 → 4 刻。"; break;
                case TutorialStep.OpponentWait:
                    explanation = "鸣钟完成！\n蓄势抽 1 张，鸣钟再抽 1 张；斥候恢复可攻击，视界 2 → 3。\n\n你到 4 刻，钟偶还在 2 刻：现在轮到它。它将静候到你的时标 +1，即第 5 刻。";
                    instruction = "点「让钟偶静候」，再观察行动权。"; break;
                default:
                    explanation = "钟偶静候到第 5 刻，你仍在第 4 刻，因此你重新获得行动权。\n\n鸣钟已让斥候重整。再造成 2 点伤害，就能将钟偶命烛降至 0。";
                    instruction = selectedUnit == 0 ? "点己方斥候，准备最后一击。" : selectedTarget.Kind == TargetKind.None ? "点上方训练钟偶作为目标。" : "点「确认行动」，完成教学。"; break;
            }
            Label(side, new Rect(18, 170, 322, 256), explanation, 21, ink);
            var task = Box(side, new Rect(14, 441, 330, 112), background);
            Label(task, new Rect(12, 10, 306, 28), "现在请这样做", 18, teal);
            Label(task, new Rect(12, 42, 306, 65), instruction, 21, gold);
            var draft = Draft();
            if (draft != null)
            {
                int cost = GameEngine.Cost(State, draft);
                previewLabel = Label(side, new Rect(18, 568, 322, 64), "支付 " + cost + " 刻：时标 " + State.Players[0].Marker + " → " + Math.Min(64, State.Players[0].Marker + cost) + "\n" + (selectedTarget.Kind == TargetKind.None ? "先看目标提示，再确认。" : "目标：训练钟偶"), 18, muted);
            }
            if (lesson.Step == TutorialStep.Chime && selectedAction != CommandKind.Focus) Button(side, new Rect(18, 651, 322, 62), "蓄势预览 · 2 → 4 刻", () => SelectAction(CommandKind.Focus), true, new Color32(96, 77, 40, 255), 24);
            else if (lesson.Step == TutorialStep.OpponentWait) Button(side, new Rect(18, 651, 322, 62), "让钟偶静候", () => Submit(lesson.ExpectedCommand()), true, new Color32(96, 77, 40, 255), 24);
            else Button(side, new Rect(18, 651, 322, 62), "确认行动", () => Submit(Draft()), LegalDraft() != null, new Color32(96, 77, 40, 255), 24);
            Label(side, new Rect(18, 731, 322, 42), message ?? "其他操作暂时锁定，避免误操作。", 16, muted);
        }
        void DrawTutorialComplete()
        {
            var box = Modal("教学完成 · 你掌握了第一段时间");
            Label(box, new Rect(40, 112, 1050, 86), "你亲手击败了训练钟偶。\n出牌、战斗与胜负均由正式规则引擎结算。", 26, teal);
            Label(box, new Rect(40, 243, 1050, 142), "记住三件事就可以开始：\n① 落后者行动；相同时，后到者先行。\n② 先结算效果，再支付时间；每到新钟点抽牌、重整、扩大视界。\n③ 攻击需要未疲惫的侍从：拖到敌人；或点来源、点目标、再确认。", 23, ink);
            Label(box, new Rect(40, 405, 1050, 64), "正式对局有起手调度和更多机制。抢拍、伏笔、咏唱可随时在「规则手册」查阅。\n建议下一步先选 AI 练习，熟悉后再与朋友热座对战。", 19, muted);
            Button(box, new Rect(40, 517, 410, 64), "开始 AI 练习", () => StartMatch(DemoMode.Practice), true, new Color32(30, 88, 85, 255), 25);
            Button(box, new Rect(480, 517, 285, 64), "重玩教学", StartTutorial, true, background, 21);
            Button(box, new Rect(795, 517, 305, 64), "返回主菜单", ShowMenu, true, background, 21);
        }
        bool TutorialCardSelectable(int id) => lesson == null || !tutorialIntro && lesson.Step == TutorialStep.Deploy && id == lesson.ScoutCardId;
        bool TutorialUnitSelectable(int owner, int id) => lesson == null || !tutorialIntro && owner == 0 && id == lesson.ScoutUnitId && (lesson.Step == TutorialStep.FirstAttack || lesson.Step == TutorialStep.FinalAttack);
        bool TargetAvailable(Target target)
        {
            if (!CanAct || Draft() == null) return false;
            var c = Draft();
            if (c.Kind == CommandKind.Omen || c.Kind == CommandKind.Focus || c.Kind == CommandKind.Wait) return false;
            c.Target = target;
            return LessonAllows(c) && GameEngine.IllegalReason(State, c) == null;
        }
        void Highlight(Button button, bool active)
        {
            if (!active) return;
            var outline = button.gameObject.AddComponent<Outline>(); outline.effectColor = gold; outline.effectDistance = new Vector2(2, -2);
        }
        void OpenRules(int page = 0)
        {
            helpPage = page; help = true;
            if (State != null) RebuildBattle(); else { ShowMenu(); help = true; DrawHelp(); }
        }
        void CloseRules()
        {
            help = false; if (State != null) RebuildBattle(); else ShowMenu();
        }
        string ActionHint()
        {
            if (!actionWindow.CanUseHand) return actionWindow.Title + "：" + actionWindow.Explanation;
            if (View.Pending?.Kind == PendingKind.Interject) return "仅可抢拍：只选金色亮边响应牌；不能普通出牌 / 攻击 / 用时器，也可放弃抢拍。";
            var draft = Draft();
            if (draft == null) return "选牌先看中央时轴；拖牌到部署区 / 目标，拖侍从到敌人。也可点击选择后确认；右键 / Esc 取消。";
            if (placingOmen) return "伏笔：直接点击时轴亮起的合法刻度，再确认；也可拖牌到该格松手提交。不是移动自己的时标。";
            string reason = GameEngine.IllegalReason(State, draft);
            if (reason == "最终时值超过视界") return "费用超过视界：等鸣钟提升上限，或使用时砂减费。也可取消，换一张便宜的牌。";
            if (reason == "请选择合法目标" || reason == "必须优先攻击守卫") return "请点金色边框的合法目标；攻击时必须先打守卫。没有高亮目标时可取消此行动。";
            if (reason == "侍从不能攻击") return "疲惫的侍从需要等本人鸣钟重整；停滞要先解除，再等下一次鸣钟。";
            if (reason != null) return reason + "。可取消选择，尝试其他行动。";
            return "准备好了：确认后先结算效果，再支付时间。时轴金色标记是预计落点，不包含抢拍与伏笔影响。";
        }
    }
}
