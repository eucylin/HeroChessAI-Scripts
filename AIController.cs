/***************************************************/
/* Author : Cloud Lin, eucylin@gmail.com
/*
/* Comment : 
 *      The AI Controller for Hero,
 *      it should only have ONE controller to control
 *      one AI team.
 * Platform :
 *      Unity3D 5.1.1f1 + UnityVS(Visual Studio Add-in)
 *      Visual Studio 2015 Enterprise
/***************************************************/
//#define AI_DEBUG
using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using FrameworkTools;

public class AIController : MonoBehaviour {
    #region some global variables
    static public BattleController bc;
    public int chessboardX;
    public int chessboardY;

    public AIHeroData aIHeroData {
        get {
            return bc.gameData.aiHeroes;
        }
    }
    //我方擁有的行動點數
    public int actionPoints {
        get {
            if (bc.nowGameTeam == GameTeam.A)
                return bc.actionPointsA;
            else
                return bc.actionPointsB;
        }
        set {
            if (bc.nowGameTeam == GameTeam.A)
                bc.actionPointsA = value;
            else
                bc.actionPointsB = value;
        }
    }

    public bool isAIRunning = false;

    //AI在StartDoAction中每個動作之間的間隔時間
    public float lowestWaitTine = 0.4f, mostWaitTime = 0.6f;

    //每項資訊佔審局影響的預設權重，權重越高，代表電腦AI下棋時會更注重於提高那部分的表現
    //例如提高血量(Heroblood)的權重， 則AI會盡可能地以保持我方高血量，敵方低血量為優先目標
    //這是預設的通用權重， 根據不同英雄模式， 可能會採用不同的自訂權重比例
    public enum Weight : int {
        HeroBlood = 10, //雙方血量 ----- //每消一點血，分數差距為(1 / 該英雄最大血量) * 100 // 4滴血的英雄打掉2滴是50分， 10滴的打掉1滴是10分，再乘上權重
        SelfBlood = 10, //此方血量
        OpBlood = 15, //對手血量
        HeroCount = 600, //場上英雄數量 ----- //消滅或多存活一隻一隻英雄，會使的分數多1分
        NexusBlood = 30, //-----每消一點血，分數差距為(1 / 主堡最大血量) * 100
        CanHit = 5, //敵人或隊友有在施放技能範圍內 ----- 可以打到的英雄 / 全部的英雄 * 100 
        CantBeHit = 10, //不會被對手的技能打中 ----- //分數為 有幾隻敵方英雄打不到你的比例 * 100, 對方有5隻英雄 3隻打不到你的艾希,分數為60分
        FarAway = 10, //與敵人步行距離
        Buff = 10, //狀態(冰凍、金人、冥想...) 包含Debuff
        NearTeammate = 5, //靠近我方友軍
        NearEnemy = 30, //靠近敵人
        NearTurret = 1100, //-----每有一個英雄在離砲塔只有一格時，加1分，離砲塔兩格，加0.5分
        ChaseCrispy = 10, //追擊脆皮英雄 (弓箭手、法師、刺客、輔助) ----- 有幾隻脆皮在攻擊範圍內 / 全部的敵方英雄 * 100
        ActionPoints = 10, //殘餘技能點數(如果對戰局沒有幫助，就不要白癡的亂放技能, ex:蓋倫空轉)
        NearTheirTauber = 20, //離對方主堡越近, 分數越高
    }

    public enum AIMode {
        Balance, //平衡
        MaxAttackDistance, //保持最大攻擊(或補血)距離，盡可能遠離敵人 (e.g.艾希) p.s.這個模式不適合攻擊距離只有1的近戰角色
        Escape, //不停逃離敵人，尋找最安全的位置 (有全場被動技能的腳色 e.g. 鎖娜、伊芙琳、凱特琳)
        MaxDamage, //保持最佳輸出位置、探索能製造最大傷害的位置(有範圍傷害或範圍補血角色 e.g. 蓋倫、好運姊)
        Tank //不停追擊後排敵人、坦傷害、限制對方後排角色輸出 (e.g. 加里歐、史瓦妮)
    }

    public enum PlayerSkillID : int {
        Heal = 101,
        Ignite = 102,
        Flash = 103,
        Gold = 104,
        Teleport = 105,
        Ghost = 106
    }

    //板凳區的判斷權重
    public enum WaitingWeight : int {
        DoDamage = 35,
        BeDamage = 15, //上場後下一回合可能會受到的傷害(落點在敵人攻擊範圍內)
        NearTeammate = 10, //靠近隊友
        NearEnemy = 20,
        NearTurret = 20
    }

    //方便取得格子四周6個相對位置座標的陣列
    Position[] sixSides = { new Position(1,0), new Position(0,1), new Position(1,1), new Position(-1,0), new Position(0,-1), new Position(-1,-1)};

    #region Shared data
    const int BIGNUM = 99999999;
    int nodeCounter;
    float timeCounter;

    List<HeroHandler> aHeroHandlerList = null; // a是現在執子的那方
    List<HeroHandler> bHeroHandlerList = null; // b是現在不能行動的那方
    List<BuildingHandler> aBuildingHandlerList = null;
    List<BuildingHandler> bBuildingHandlerList = null;
    List<int> aWaitingListID = null;
    List<int> bWaitingListID = null;
    List<int> aUnLockedWaitListID = null;
    List<int> bUnLockedWaitListID = null;
    HeroHandler aNexusAttackTarget {
        get {
            if (bc.nowGameTeam == GameTeam.A)
                return bc.p1AttackHeroId;
            else if (bc.nowGameTeam == GameTeam.B)
                return bc.p2AttackHeroId;
            else
                return null;
        }
    }
    HeroHandler bNexusAttackTarget {
        get {
            if (bc.nowGameTeam == GameTeam.A)
                return bc.p2AttackHeroId;
            else if (bc.nowGameTeam == GameTeam.B)
                return bc.p1AttackHeroId;
            else
                return null;
        }
    }
    Image[] psi; //召喚師技能 (PlayerSkillImageArray)

    //搜尋完成，要在這一回合被StartDoAction開始進行真實動作的組合
    List<Action> actionToBeDone;
    //要在這一回合中使用的召喚師技能
    List<Action> before_playerSkillToBeDone;
    //要在這一回合末(從板凳區拉人上場前)使用的召喚師技能 (點燃、治癒、金人、傳送)
    List<Action> after_playerSkillToBeDone;
    //搜尋完成後判定最適合這回合上場的英雄ID(我們用ID來從等待區指派上場英雄)
    int bestWaitingHeroID;
    //最適合上場的位置for該英雄
    int[] bestWaitingHeroPosXY;
    int bestWaitingIndex = -1;
    HeroHandler oneRoundKillHero;
    int damageDelay = 0;
    List<HeroHandler> skipSearchHero;

    int[] teleportPos = null;


    public struct EfficiencyCounter {
        public float traverse;
        public float fakemove;
        public float doheromove;
        public float doheromove_NoCoroutine;
        public float doheroskill;
        public float playerskill;
        public float waitinghero;
        public float loadrecorddata;
        public float loadrecorddatahero;
        public float recorddata;
        public float evaluate;
        public void Reset() {
            traverse = 0; fakemove = 0; doheromove = 0; doheromove_NoCoroutine = 0; doheroskill = 0; playerskill = 0;
            waitinghero = 0; loadrecorddata = 0; loadrecorddatahero = 0; recorddata = 0; evaluate = 0;
        }
    }
    public EfficiencyCounter ec;
    #endregion

    #region Action Data Strcture
    public enum ActionType {
        UseSummonerSkill, CallWaitingHero, HeroMove, HeroSkill, DoNothing
    }

    //一個"動作"，例如把英雄從A移動到B，讓英雄在A點往B點放技能、對B點的的英雄使用召喚師技能各算一個動作
    struct Action {
        public ActionType type;
        public int[] userPos;
        public int[] targetPos;
        public int costPoints;//消耗行動點數
        public Action(ActionType _type, int[] _userPos = null, int[] _targetPos = null, int _costPoints = 0){
            type = _type; userPos = _userPos; targetPos = _targetPos;
            if (_costPoints == 0) {
                switch (type) {
                    case ActionType.CallWaitingHero:
                        costPoints = ActionPointToXY(new Position(targetPos[0], targetPos[1]));                        
                        break;
                    case ActionType.HeroSkill:
                    case ActionType.HeroMove:
                    default:
                        costPoints = 1;
                        break;
                }                
            } else {
                costPoints = _costPoints;
            }            
        }
    }

    //紀錄搜尋結果
    class SearchActionInfo {
        public Action action;
        public float addScore;//分數變化
        public EvaluateScoreInfo checkEvaluate;
        public bool isMove = false;
        public bool isSkill = false;
        public int userNewPos = 0;// = 原座標 userPos[1]*chessboardX + userPos[0]
        public int targetNewPos = 0;
        public SearchActionInfo(Action setAction, float setAddScore) {
            action = setAction;
            addScore = setAddScore;
            //移動類, 包含HeroMove, 英雄登場, 招喚師技能的閃現,衝鋒,鬼步..
            //技能類, 包含HeroSkill, 招喚師技能的點燃,治療,金鐘
            switch (action.type) {
                case ActionType.HeroMove:
                case ActionType.CallWaitingHero:
                    isMove = true;
                    userNewPos = bc.chessboardY * bc.chessboardX + action.userPos[0];//待命區
                    targetNewPos = action.targetPos[1] * bc.chessboardX + action.targetPos[0];
                    break;
                case ActionType.HeroSkill:
                    isSkill = true;
                    userNewPos = action.userPos[1] * bc.chessboardX + action.userPos[0];
                    targetNewPos = action.targetPos[1] * bc.chessboardX + action.targetPos[0];
                    break;
                case ActionType.UseSummonerSkill:
                    switch ((PlayerSkillID)action.userPos[0]) {
                        case PlayerSkillID.Teleport:
                            isMove = true;
                            userNewPos = bc.chessboardY * bc.chessboardX + action.userPos[1];//待命區
                            targetNewPos = action.targetPos[1] * bc.chessboardX + action.targetPos[0];
                            break;
                        case PlayerSkillID.Flash:
                        case PlayerSkillID.Ghost:
                            isMove = true;
                            userNewPos = action.userPos[2] * bc.chessboardX + action.userPos[1];
                            targetNewPos = action.targetPos[1] * bc.chessboardX + action.targetPos[0];
                            break;
                        case PlayerSkillID.Ignite:
                        case PlayerSkillID.Heal:
                        case PlayerSkillID.Gold:
                            isSkill = true;
                            userNewPos = -1;//不需userPos
                            targetNewPos = action.targetPos[1] * bc.chessboardX + action.targetPos[0];
                            break;
                    }
                    break;
            }
        }
        public float deltaScore {
            get {
                return (action.costPoints > 0) ? addScore / (float)action.costPoints : 0f;
            }
        }

    }

    //for debug 審局函數
    class EvaluateScoreInfo {
        public float sTotal, sHeroCount, sTurretDistance, sBeAttacked, sAttackAnyOne, sFriendAnyOne;
        public EvaluateScoreInfo(float setHeroCount, float setTurretDistance, float setBeAttacked, float setAttackAnyOne, float setFriendAnyOne) {
            sHeroCount = setHeroCount;
            sTurretDistance = setTurretDistance;
            sBeAttacked = setBeAttacked;
            sAttackAnyOne = setAttackAnyOne;
            sFriendAnyOne = setFriendAnyOne;
            sTotal = sHeroCount + sTurretDistance + sBeAttacked + sAttackAnyOne + sFriendAnyOne;
        }
        public override string ToString() {
            return "總分數:"+ sTotal + " 子力:" + sHeroCount + " 位置:" + sTurretDistance + " 安全:" + sBeAttacked + " 攻擊:" + sAttackAnyOne + " 配合:" + sFriendAnyOne;
        }
    }

    struct Position {
        public int x;
        public int y;
        public Position(Position pos) { x = pos.x; y = pos.y; }
        public Position(int _x, int _y) { x = _x; y = _y; }
        public Position(int[] posArray) { x = posArray[0]; y = posArray[1]; }

        public static Position operator+(Position p1, Position p2) {
            return new Position(p1.x + p2.x, p1.y + p2.y);
        }
        public int[] ToArray() {
            int[] array = new int[2] { x, y };
            return array;
        }
    }
    #endregion

    #endregion

    #region Main Function
    public IEnumerator StartAI() {
        if (isAIRunning == false) {
            isAIRunning = true;
            timeCounter = Time.realtimeSinceStartup;
            ec.Reset();

            //尋找最佳著法
            yield return StartCoroutine(StartSearching());

            Debug.Log("Search time : " + (Time.realtimeSinceStartup - timeCounter) + "  NodeNum : " + nodeCounter);

            Debug.Log(string.Format("Traverse: {0}, Fakemove: {1}, Evaluate {2}, WaitHero {3}, PlayerSkill {4}, Record {5}, LoadRecord {6}, LoadRecordHero {7}, DoHeroMove_NoCor {8}, DoHeroMove {9}, DoHeroSkill {10}",
                                        ec.traverse, ec.fakemove, ec.evaluate, ec.waitinghero, ec.playerskill, ec.recorddata, ec.loadrecorddata, ec.loadrecorddatahero, ec.doheromove_NoCoroutine, ec.doheromove, ec.doheroskill));

            //AI開始進行英雄部分的動作(技能&移動)，要讓AI動得像個人，所以動作間有刻意的延遲
            //要進行哪些動作，已經在StartSearch裡決定了
            yield return StartCoroutine(StartDoAction(actionToBeDone));

            isAIRunning = false;
        }
        yield break;
    }

    IEnumerator StartSearching() {
        bc.isAI = true;
        nodeCounter = 0;
        actionToBeDone = new List<Action>();
        skipSearchHero = new List<HeroHandler>();
        int backupIndex = bc.RecordData(); //備份棋盤資料

        DecideWhichTeam(); //決定現在輪到誰
        DecideWhichHeroFirst(); //決定哪知英雄要先被搜尋

        ////一般殺敵模式才啟用OneRoundKill(因為主堡模式，狂殺人不一定有幫助)
        while (CanOneRoundKill())
            yield return StartCoroutine(OneRoundKillSearch());

        List<SearchActionInfo> searchActionInfoList = new List<SearchActionInfo>();
        while (actionPoints > 0) {
            DecideWhichTeam();
            //先把所有的使用行動點方式列出 (上場 移動 攻擊..)
            List<Action> actionList = GenAllActionList();
            searchActionInfoList.Clear();

            //實作每個動作，並審局
            Action bestAction = new Action(ActionType.DoNothing);
            int recordIndex = bc.RecordData();

            float maxDeltaScore = 0;
            EvaluateScoreInfo checkEvaluate;
            float beforeScore = EvaluateByFish(out checkEvaluate);//執行動作前的審局分數
            Debug.Log(string.Format("=====行動前總分數:{0} 子力:{1} 位置:{2} 安全:{3} 攻擊:{4} 配合:{5} 剩餘行動點:{6}", beforeScore, checkEvaluate.sHeroCount, checkEvaluate.sTurretDistance, checkEvaluate.sBeAttacked, checkEvaluate.sAttackAnyOne, checkEvaluate.sFriendAnyOne, actionPoints));

            foreach (Action action in actionList) {
                if (action.costPoints <= 0 || action.costPoints > actionPoints) {//萬一有怪怪的點數, 跳過
                    continue;
                }

                //實作這個動作
                switch (action.type) {
                    case ActionType.DoNothing:
                    case ActionType.HeroMove:
                    case ActionType.CallWaitingHero:
                    case ActionType.UseSummonerSkill:
                        //盡量都跑Fast
                        AIFakeMove_Fast(action);
                        break;
                    default:
                        yield return StartCoroutine(AIFakeMove(new Action[] { action }));
                        break;
                }                
                float afterScore = EvaluateByFish(out checkEvaluate);
                //紀錄每一步走法搜尋結果
                SearchActionInfo searchActionInfo = new SearchActionInfo(action, afterScore - beforeScore);
                searchActionInfo.checkEvaluate = checkEvaluate;
                searchActionInfoList.Add(searchActionInfo);
                
                bc.LoadRecordData(recordIndex, true); //復原棋盤
            }
            //由步走法搜尋結果, 用背包算法找出最佳解
            FindBestActionScore(searchActionInfoList, actionPoints, out bestAction);

            //若未行動則跳出
            if (bestAction.type == ActionType.DoNothing) {
                Debug.Log("best action is Do Nothing!! score :" + maxDeltaScore);
                break;
            }

            switch (bestAction.type) {
                case ActionType.HeroMove:
                    HeroHandler mhero = bc.chessboardYX[bestAction.userPos[1], bestAction.userPos[0], (int)BattleController.ChessboardItem.Unit].GetComponent<HeroHandler>();
                    Debug.Log(string.Format("執行 AI移動 {0} 目標: ({1}, {2}) 執行前剩餘行動點: {3}", mhero.heroDataItem.description, bestAction.targetPos[0], bestAction.targetPos[1], actionPoints));
                    break;
                case ActionType.HeroSkill:
                    HeroHandler shero = bc.chessboardYX[bestAction.userPos[1], bestAction.userPos[0], (int)BattleController.ChessboardItem.Unit].GetComponent<HeroHandler>();
                    Debug.Log(string.Format("執行 AI放技能 {0} 目標: ({1}, {2}) 執行前剩餘行動點: {3}", shero.heroDataItem.description, bestAction.targetPos[0], bestAction.targetPos[1], actionPoints));
                    break;
                case ActionType.CallWaitingHero:
                    string log = string.Format("執行 AI召喚上場 : {0}, 上場位置 : ({1}, {2}) 執行前剩餘行動點: {3}", HeroData.ItemDict[bestAction.userPos[0]].description, bestAction.targetPos[0], bestAction.targetPos[1], actionPoints);
                    Debug.Log(log);
                    break;
                case ActionType.UseSummonerSkill:
                    Debug.Log(string.Format("執行 AI召喚師技能 : {0}, 目標 : ({1}, {2})", FindPlayerSkill((PlayerSkillID)bestAction.userPos[0]).playerSkillDataItem.name, bestAction.targetPos[0], bestAction.targetPos[1]));
                    break;
            }

            //實作動作，進入下一次迴圈(若還有行動點的話)
            if (bestAction.type != ActionType.HeroSkill) //除了HeroSkill外其他都用非Coroutine的AI版，速度快上很多
                AIFakeMove_Fast(bestAction);
            else
                yield return StartCoroutine(AIFakeMove(new Action[] { bestAction }));
            

            //把動作加入最終的list, 會在StartDoAction執行給玩家看
            actionToBeDone.Add(bestAction);
            
        }


        //yield return StartCoroutine(TraverseEachHeroAction(aHeroHandlerList));

        ////接下來我們要搜尋 1.部分召喚師技能(after系列，後使用的那些) 2.板凳區的英雄誰最適合上場、在哪個位置上場
        ////after系列召喚技能搜尋，並實作
        //yield return StartCoroutine(After_PlayerSkillSearch());
        ////將搜尋完的結果加入List裡，待會一併用StartDoAciton完成
        //actionToBeDone.AddRange(after_playerSkillToBeDone);

        ////決定板凳區的哪位英雄要在哪個位置上場
        //while (actionPoints >= 2 && aUnLockedWaitListID.Count > 0 && NowHeroOnChessboard(bc.nowGameTeam) < 5) {
        //    DecideWhichTeam(); //把英雄召喚上場後要重新更新名單，不然剛上場的英雄還會在aUnlockedWaitList
        //    WaitingHeroSearching();
        //    if (bestWaitingHeroID > 0  && bestWaitingHeroPosXY[0] != -1 && bestWaitingHeroPosXY[1] != -1) {
        //        string log = string.Format("AI上場英雄 : {0}, 上場位置 : ({1}, {2})", HeroData.ItemDict[bestWaitingHeroID].description, bestWaitingHeroPosXY[0], bestWaitingHeroPosXY[1]);
        //        Debug.Log(log);
        //        bc.lockID = bestWaitingIndex;
        //        yield return StartCoroutine(bc.AddWaitingHeroToChessboardID(bc.nowGameTeam, bc.nowGameTeam, bestWaitingHeroPosXY[0], bestWaitingHeroPosXY[1], true, true, true));
        //        actionToBeDone.Add(new Action(ActionType.CallWaitingHero, new int[] { bestWaitingIndex }, bestWaitingHeroPosXY));
        //    }
        //}

        bc.LoadRecordData(backupIndex, true); //還原棋盤資料
        FinishSearch();
        yield break;
    }
    #endregion

    //背包演算法找出最佳解
    float FindBestActionScore(List<SearchActionInfo> searchActionInfoList, int totalCostPoint, out Action best_action) {
        //先照cp值排序(高->低)
        searchActionInfoList = searchActionInfoList.OrderByDescending(x => x.deltaScore).ToList();

        //重複的部分不計(同一英雄找移動best, skill best, call best)
        List<SearchActionInfo> actionCheckList = new List<SearchActionInfo>();
        foreach (SearchActionInfo actionInfo in searchActionInfoList) {
            //分數小於等於0的可以考慮不計
            /*
            if (actionInfo.addScore <= 0) {
                continue;
            }
            */
            //檢查重複
            bool isAdd = true;
            foreach (SearchActionInfo actionCheck in actionCheckList) {
                //同一英雄移動
                if (actionInfo.action.type == ActionType.HeroMove
                    && actionCheck.action.type == ActionType.HeroMove
                    && actionInfo.action.userPos[0] == actionCheck.action.userPos[0]
                    && actionInfo.action.userPos[1] == actionCheck.action.userPos[1]) {
                    isAdd = false;
                    break;
                }
                //同一英雄放技能
                if (actionInfo.action.type == ActionType.HeroSkill
                    && actionCheck.action.type == ActionType.HeroSkill
                    && actionInfo.action.userPos[0] == actionCheck.action.userPos[0]
                    && actionInfo.action.userPos[1] == actionCheck.action.userPos[1]) {
                    isAdd = false;
                    break;
                }
                //同一英雄登場 or 登場在重複位置
                if (actionInfo.action.type == ActionType.CallWaitingHero
                    && actionCheck.action.type == ActionType.CallWaitingHero
                    && (actionInfo.action.userPos[0] == actionCheck.action.userPos[0] 
                        || (actionInfo.action.targetPos[0] == actionCheck.action.targetPos[0] 
                            && actionInfo.action.targetPos[1] == actionCheck.action.targetPos[1])
                            )
                    ){
                    isAdd = false;
                    break;
                }
                //目標位置重複(移動,登場(含招喚師技能:閃現,鬼步,衝鋒) != 移動,登場(含招喚師技能:閃現,鬼步,衝鋒),放技能)
                if (actionInfo.isMove && (actionCheck.isMove || actionCheck.isSkill)
                    && actionInfo.action.targetPos[0] == actionCheck.action.targetPos[0]
                    && actionInfo.action.targetPos[1] == actionCheck.action.targetPos[1]
                    ) {
                    isAdd = false;
                    break;
                }

                //施放同一召喚師技能
                if (actionInfo.action.type == ActionType.UseSummonerSkill
                    && actionCheck.action.type == ActionType.UseSummonerSkill
                    && actionInfo.action.userPos[0] == actionCheck.action.userPos[0]) {
                    isAdd = false;
                    break;
                }

                //一英雄最多一移動（包括move,summon,...）
                if (actionInfo.isMove && actionCheck.isMove && actionInfo.userNewPos == actionCheck.userNewPos) {
                    isAdd = false;
                    break;
                }
                //一英雄最多一攻擊（包括skill,summon,...）. 若無userPos, 不須比較
                if (actionInfo.isSkill && actionCheck.isSkill && actionInfo.userNewPos>=0 && actionInfo.userNewPos == actionCheck.userNewPos) {
                    isAdd = false;
                    break;
                }

            }
            //沒重複就加吧
            if (isAdd) {
                actionCheckList.Add(actionInfo);
            }
        }

        //照costPoint分類
        //消耗1,2,3...10點分類排序[index 0 1 2...9]
        //萬一超過10點? 就不管他了
        const int maxNumCostPoints = 10;
        List<SearchActionInfo>[] actionListCost = new List<SearchActionInfo>[maxNumCostPoints];
        for(int i=0; i< actionListCost.Length; i++){
            actionListCost[i] = new List<SearchActionInfo>();
        };
        foreach (SearchActionInfo searchAction in actionCheckList) {
            if (searchAction.action.costPoints >= 1 && searchAction.action.costPoints <= maxNumCostPoints) {
                actionListCost[searchAction.action.costPoints - 1].Add(searchAction);
            }
        }

        //log
        foreach (List<SearchActionInfo> actionList in actionListCost) {
            foreach (SearchActionInfo searchAction in actionList) {
                Action action = searchAction.action;
                HeroHandler logHero = null;
                switch (action.type) {
                    case ActionType.HeroMove:
                        logHero = bc.chessboardYX[action.userPos[1], action.userPos[0], (int)BattleController.ChessboardItem.Unit].GetComponent<HeroHandler>();
                        Debug.Log(string.Format("AI移動 {0} 原點:({1}, {2}) 目標: ({3}, {4}) 加分: {5} 消耗行動點:{6} 執行前剩餘行動點:{7} \n 分數詳情=> {8}"
                            , logHero.heroDataItem.description, action.userPos[0], action.userPos[1], action.targetPos[0], action.targetPos[1], searchAction.addScore, searchAction.action.costPoints, totalCostPoint, searchAction.checkEvaluate.ToString()));                        
                        break;
                    case ActionType.HeroSkill:
                        logHero = bc.chessboardYX[action.userPos[1], action.userPos[0], (int)BattleController.ChessboardItem.Unit].GetComponent<HeroHandler>();
                        Debug.Log(string.Format("AI放技能 {0} 原點:({1}, {2}) 目標: ({3}, {4}) 加分: {5} 消耗行動點:{6} 執行前剩餘行動點:{7} \n 分數詳情=> {8}", logHero.heroDataItem.description, action.userPos[0], action.userPos[1], action.targetPos[0], action.targetPos[1], searchAction.addScore, searchAction.action.costPoints, totalCostPoint, searchAction.checkEvaluate.ToString()));
                        break;
                    case ActionType.CallWaitingHero:
                        string log = string.Format("AI召喚上場 : {0}, 上場位置 : ({1}, {2}) 加分: {3} 消耗行動點:{4} 執行前剩餘行動點:{5} \n 分數詳情=> {6}", HeroData.ItemDict[action.userPos[0]].description, action.targetPos[0], action.targetPos[1], searchAction.addScore, searchAction.action.costPoints, totalCostPoint, searchAction.checkEvaluate.ToString());
                        Debug.Log(log);
                        break;
                    case ActionType.UseSummonerSkill:
                        if (action.userPos[0] == (int)PlayerSkillID.Flash || action.userPos[0] == (int)PlayerSkillID.Ghost) {
                            Debug.Log(string.Format("AI召喚師技能 : {0}, 原點:({1}, {2}) 目標 : ({3}, {4}) 加分: {5} 消耗行動點:{6} 執行前剩餘行動點:{7} \n 分數詳情=> {8}", FindPlayerSkill((PlayerSkillID)action.userPos[0]).playerSkillDataItem.name, action.userPos[1], action.userPos[2], action.targetPos[0], action.targetPos[1], searchAction.addScore, searchAction.action.costPoints, totalCostPoint, searchAction.checkEvaluate.ToString()));
                        } else {
                            Debug.Log(string.Format("AI召喚師技能 : {0}, 目標 : ({1}, {2}) 加分: {3} 消耗行動點:{4} 執行前剩餘行動點:{5} \n 分數詳情=> {6}", FindPlayerSkill((PlayerSkillID)action.userPos[0]).playerSkillDataItem.name, action.targetPos[0], action.targetPos[1], searchAction.addScore, searchAction.action.costPoints, totalCostPoint, searchAction.checkEvaluate.ToString()));
                        }                        
                        break;
                }

            }
        }


        //遞迴背包
        SearchActionInfo searchBestAction;
        float score = FindBagScore(actionListCost, totalCostPoint, maxNumCostPoints, out searchBestAction);
        best_action = searchBestAction.action;

        return score;
    }

    //背包遞迴
    float FindBagScore(List<SearchActionInfo>[] actionListCost, int totalCostPoint, int maxCostPoint, out SearchActionInfo thisBestAction) {
        thisBestAction = new SearchActionInfo(new Action(ActionType.DoNothing), 0f);
        float maxAddScore = 0f;

        SearchActionInfo searchBestAction;
        float score;
        SearchActionInfo thisAction;
        //依消耗點數分類, 逐步搜尋如: 4 31 22 211 1111 ....

        //for (int thisCostPoint = 1; thisCostPoint <= maxCostPoint; thisCostPoint++) {            
        for (int thisCostPoint = maxCostPoint; thisCostPoint >= 1; thisCostPoint--) {
            if (totalCostPoint >= thisCostPoint && actionListCost[thisCostPoint - 1].Count>0) {
                thisAction = actionListCost[thisCostPoint - 1][0];
                actionListCost[thisCostPoint - 1].RemoveAt(0);//已使用
                score = thisAction.addScore + FindBagScore(actionListCost, totalCostPoint - thisCostPoint, thisCostPoint, out searchBestAction);
                actionListCost[thisCostPoint - 1].Insert(0, thisAction);//復原

                if (score > maxAddScore) {
                    maxAddScore = score;
                    //最後移動順序: skill->move(包含招喚師技能..)
                    if (thisAction.isSkill && searchBestAction.isMove) {
                        thisBestAction = thisAction;
                    } else if (thisAction.isMove && searchBestAction.isSkill) {
                        thisBestAction = searchBestAction;
                    } else {
                        thisBestAction = (thisAction.deltaScore > searchBestAction.deltaScore) ? thisAction : searchBestAction;
                    }                    
                }
            }
        }
        return maxAddScore;
    }

    //列出當前盤面所有可以使用行動點的動作
    List<Action> GenAllActionList() {
        List<Action> result = new List<Action>();
        int rdIndex = bc.RecordData(); //backup all data in game, and get a index.

        // A + B. 3點上場與兩點上場
        int aRealHeroCount = 0;//算真英雄數量, 不算招喚物&小兵
        for (int lCount = 0; lCount < aHeroHandlerList.Count; lCount++) {
            if (aHeroHandlerList[lCount].isSummon == 0) {
                aRealHeroCount++;
            }
        }
        if (aRealHeroCount < 5) {//場上英雄數量小於5才能上場
            bc.UpdateBoardHeroActionArrayXY(ActionType.CallWaitingHero);
            for (int wY = 0; wY < chessboardY; wY++)
                for (int wX = 0; wX < chessboardX; wX++)
                    if (bc.boardHeroActionArrayXY[wX, wY, (int)BoardHeroActionArrayItem.BoardHeroAction] == (int)BoardHeroAction.CanAddWaitingHero) {
                        for (int i = 0; i < aUnLockedWaitListID.Count; i++)
                            result.Add(new Action(ActionType.CallWaitingHero, new int[] { aUnLockedWaitListID[i] }, new int[] { wX, wY }));
                    }
        }
        // C. 移動 / 攻擊
        List<HeroHandler> hh = aHeroHandlerList;
        for (int i = 0; i < hh.Count; i++) {
            if (hh[i] == null) continue; //他可能被好運姊陰死了(被隊友殺死屬於例外)

            //1.skill
            if (HeroCanSkill(hh[i])) {
                bc.UpdateBoardHeroActionArrayXY(ActionType.HeroSkill, hh[i].PositionXY[0], hh[i].PositionXY[1]);
                for (int sY = 0; sY < chessboardY; sY++)
                    for (int sX = 0; sX < chessboardX; sX++)
                        if (bc.boardHeroActionArrayXY[sX, sY, (int)BoardHeroActionArrayItem.BoardHeroAction] == (int)BoardHeroAction.CanSkill) {
                            //this is skill but don't move
                            Action heroSkill = new Action(ActionType.HeroSkill,
                                new int[2] { hh[i].PositionXY[0], hh[i].PositionXY[1] }, new int[] { sX, sY });
                            result.Add(heroSkill);
                        }
            }

            //2.move            
            if (HeroCanMove(hh[i])) {
                bc.UpdateBoardHeroActionArrayXY(ActionType.HeroMove, hh[i].PositionXY[0], hh[i].PositionXY[1]);
                for (int mY = 0; mY < chessboardY; mY++)
                    for (int mX = 0; mX < chessboardX; mX++)
                        if (bc.boardHeroActionArrayXY[mX, mY, (int)BoardHeroActionArrayItem.BoardHeroAction] == (int)BoardHeroAction.CanMove) {
                            result.Add(new Action(ActionType.HeroMove,
                                new int[2] { hh[i].PositionXY[0], hh[i].PositionXY[1] }, new int[] { mX, mY }));
                        }
            }

        }

        // D. 召喚師技能(消耗[nowCD]點行動點)
        PlayerSkillHandler ignite = FindPlayerSkill(PlayerSkillID.Ignite);
        if (ignite != null) //點燃
        {
            foreach (HeroHandler bhh in bHeroHandlerList) { //每個可點燃的腳色都加入list，嘗試點燃
                if (IsHeroAttackable(bhh) && bhh.nowHP<=2) {//try 可燒死的才加// && bhh.nowHP==1
                    Action action_Ignite = new Action(ActionType.UseSummonerSkill, new int[] { ignite.playerSkillID }, bhh.PositionXY, ignite.nowCD);
                    result.Add(action_Ignite);
                }
            }
        }

        PlayerSkillHandler heal = FindPlayerSkill(PlayerSkillID.Heal);
        if (heal != null) //治癒
        {
            foreach(HeroHandler ahh in aHeroHandlerList) {
                if (IsHeroHealable(ahh) && ahh.nowHP < ahh.maxHP) {//沒滿血的才補// && ahh.nowHP<=ahh.maxHP-2
                    Action action_Heal = new Action(ActionType.UseSummonerSkill, new int[] { heal.playerSkillID }, ahh.PositionXY, heal.nowCD);
                    result.Add(action_Heal);
                }
            }
        }

        //閃現
        PlayerSkillHandler flash = FindPlayerSkill(PlayerSkillID.Flash);
        if (flash != null) {
            foreach (HeroHandler ahh in aHeroHandlerList) {
                bc.UsePlayerSkill(flash, bc.nowGameTeam);
                bc.nowCursorX = ahh.PositionXY[0]; bc.nowCursorY = ahh.PositionXY[1];
                bc.DoPlayerSkill_ForAI();
                for (int y = 0; y < chessboardY; y++)
                    for(int x = 0; x < chessboardX; x++) {
                        if(bc.boardHeroActionArrayXY[x, y, (int)BoardHeroActionArrayItem.BoardHeroAction] == (int)BoardHeroAction.CanPlayerSkill) {
                            Action action_Flash = new Action(ActionType.UseSummonerSkill, new int[] { flash.playerSkillID, ahh.PositionXY[0], ahh.PositionXY[1] }, new int[] { x, y }, flash.nowCD);
                            result.Add(action_Flash);
                        }
                    }
            }
        }

        //鬼步
        PlayerSkillHandler ghost = FindPlayerSkill(PlayerSkillID.Ghost);
        if (ghost != null) 
        {
            int ori_actionPoints = actionPoints;
            int ori_ghostCD = ghost.nowCD;

            foreach (HeroHandler ahh in aHeroHandlerList) {
                int ori_movePoint = ahh.nowMovePoint;
                List<BuffHandler> ori_buffList = new List<BuffHandler>(ahh.buffList);

                bc.UsePlayerSkill(ghost, bc.nowGameTeam);
                bc.nowCursorX = ahh.PositionXY[0]; bc.nowCursorY = ahh.PositionXY[1];
                bc.DoPlayerSkill_ForAI();

                bc.UpdateBoardHeroActionArrayXY(ActionType.HeroMove, ahh.PositionXY[0], ahh.PositionXY[1]);
                for (int y = 0; y < chessboardY; y++)
                    for (int x = 0; x < chessboardX; x++) {
                        if (bc.boardHeroActionArrayXY[x, y, (int)BoardHeroActionArrayItem.BoardHeroAction] == (int)BoardHeroAction.CanMove) {
                            //使用鬼步消耗ori_ghostCD點行動點, 移動消耗1點, 所以costPoints = ori_ghostCD+1
                            Action action_Ghost = new Action(ActionType.UseSummonerSkill, new int[] { ghost.playerSkillID, ahh.PositionXY[0], ahh.PositionXY[1] }, new int[] { x, y }, ori_ghostCD+1);
                            result.Add(action_Ghost);
                        }
                    }

                //鬼步比較特別, 會真正使用他來找出可以移動的格子, 所以每找完一個英雄要LoadRecordData把cd跟行動點等等還原
                actionPoints = ori_actionPoints;
                ghost.nowCD = ori_ghostCD;
                ahh.nowMovePoint = ori_movePoint;
                ahh.buffList = ori_buffList;
                //bc.LoadRecordData(rdIndex, true);  //無法在Iteration中改變值 (aHeroHandlerList)
            }
        }

        bc.LoadRecordData(rdIndex, true);
        return result;
    }

    #region Evaluate
    //魚大哥定的評估函數
    float EvaluateByFish(out EvaluateScoreInfo checkEvaluate) {
        float timer = Time.realtimeSinceStartup;
        float score = 0;
        float score_HeroCount, score_TurretDistance, score_BeAttacked, score_AttackAnyOne, score_FriendAnyOne;
        //子力分數
        score += (score_HeroCount=GetScore_HeroCount(1f));
        //位置分數
        score += (score_TurretDistance=GetScore_TurretDistance(1f));
        //安全分數
        score += (score_BeAttacked=GetScore_BeAttacked(1f));
        //攻擊分數
        score += (score_AttackAnyOne=GetScore_AttackAnyOne(1f));
        //配合分數
        score += (score_FriendAnyOne=GetScore_FriendAnyOne(1f));

        ec.evaluate += Time.realtimeSinceStartup - timer;
        checkEvaluate = new EvaluateScoreInfo(score_HeroCount, score_TurretDistance, score_BeAttacked, score_AttackAnyOne, score_FriendAnyOne);
        return score;
    }
    /***************************************************/
    /* 局面評估函數，檢視"現在"這個局面的分數。分數越高代表對AI方越有利
     * 我們會同時評估雙方的分數 (aiScore和opScore), 
     * 以分差(ai-op / ai+op)乘上權重(weight)作為最後的總分
     * 所以不會有搜尋深度奇偶性問題

    /######所有的分數 都應該是介於 0~100 的整數值，最後才呈上權重#####/
    /***************************************************/
    int Evaluate(AIHeroData.AIMode mode = AIHeroData.AIMode.Balance, HeroHandler hero = null) {
        //for Efficiency Counter
        float timer = Time.realtimeSinceStartup;

        float score = 0;
        float aScore = 0; // A是現在可以行動的這方 (此方)
        float bScore = 0; // B是對手方、現在不能動作的這方 (彼方)

        ////////////////////////////////////////////////////////////////////////
        ////////////////////////////////////////////////////////////////////////
        if (hero != null) {
            switch (mode) {
                default:
                case AIHeroData.AIMode.Balance:
                    //雙方血量
                    //每消一點血，分數差距為(1 / 該英雄最大血量) * 100 * 權重 ~= 100~500
                    //4滴血的英雄打掉2滴是50分， 10滴的打掉1滴是10分，在乘上權重
                    score += Evaluate_HeroBlood((int)Weight.HeroBlood);

                    //場上英雄數量
                    //消滅一隻英雄，會使的分數多600分
                    score += Evaluate_HeroCount((int)Weight.HeroCount);

                    //此方與敵人英雄距離 (靠近彼方敵人加分)
                    score += Evaluate_NearEnemy((int)Weight.NearEnemy);

                    score += Evaluate_NexusBlood((int)Weight.NexusBlood);

                    score += Evaluate_NearTurret((int)Weight.NearTurret, 2);

                    score += Evaluate_ActionPoints((int)Weight.ActionPoints);

                    score += Evaluate_NearTheirTauber((int)Weight.NearTheirTauber);
                    break;
                case AIHeroData.AIMode.MaxAttackDistance:
                    //當越多英雄處於你的技能範圍內時分數會越高
                    //分數為 (你能打到or補到的英雄 / 全部的英雄) * 100 * 權重 ~= 250 ~ 800
                    //每多一隻英雄多 200~400分左右
                    int CanHit_Score = 0;
                    foreach (HeroHandler targetToBeSkilled in bc.allHeroHandlerList) //我們技能可能可以對隊友施放，所以要檢查allHero
                        if (ACanHitB(hero, targetToBeSkilled, SkillDistanceType.Any))
                            CanHit_Score += (int)((1.0f / bc.allHeroHandlerList.Count) * 100); //會產生0~100的分數
                    score += CanHit_Score * (int)Weight.CanHit;

                    //同時也要盡可能的不要楚瑜在敵人的攻擊範圍內
                    //分數為 有幾隻敵方英雄打得道你的百分比 * 權重, 每增加一隻多150~300分數
                    //[TODO]敵人打不打的到只考慮現在的距離，但其實他下一回合可以靠近你一步再肛你
                    score += Evaluate_CantBeHit((int)Weight.CantBeHit, hero);

                    //因為你可能會把敵人做掉，這麼做會減少CanHit對象的數量，進而減少CanHit評分，但其實不應該減少分數
                    //所以我們還是得評估雙方血量， 在這邊把因擊殺敵人而造成的分數減少補回來
                    //每消一點血，分數差距為(1 / 該英雄最大血量) * 100 * 權重 ~= 100~500
                    score += Evaluate_HeroBlood((int)Weight.HeroBlood);

                    score += Evaluate_NearEnemy((int)Weight.NearEnemy / 2);

                    score += Evaluate_NexusBlood((int)Weight.NexusBlood);

                    score += Evaluate_NearTurret((int)Weight.NearTurret, 2);

                    score += Evaluate_ActionPoints((int)Weight.ActionPoints);

                    score += Evaluate_NearTheirTauber((int)Weight.NearTheirTauber);
                    break;

                case AIHeroData.AIMode.Escape:
                    //別讓其他英雄打到我
                    //分數為 有幾隻敵方英雄打得道你的百分比 * 權重,
                    score += Evaluate_CantBeHit((int)Weight.CantBeHit, hero);

                    //同時也要尋找一個空曠的，距離各個敵人都很遠的地點隱居
                    //[TODO] 這個分數無法化為 0~100的區間， 只能猜大概
                    //每遠離1個敵人1距離，1.5 ~ 3.5 * 權重 ~= 40 ~ 90
                    //aScore = 0; bScore = 0;
                    //bc.AIUpdateHeroDistance(hero);
                    //foreach (HeroHandler ophh in bHeroHandlerList) {
                    //    int distance = bc.boardHeroActionArrayXY[ophh.PositionXY[0], ophh.PositionXY[1], (int)BoardHeroActionArrayItem.Distance];
                    //    aScore += (float)distance / chessboardX * 100 / bHeroHandlerList.Count;
                    //}

                    //int FarAway_Score = (int)aScore;
                    //score += FarAway_Score * (int)Weight.FarAway;

                    //別忘了順手打一下敵人，或是補一下隊友
                    //調整過的權重，每消一點血，分數差距為(1 / 該英雄最大血量) * 100 * 權重 ~= 50~250
                    score += Evaluate_HeroBlood((int)Weight.HeroBlood / 2);

                    score += Evaluate_NearEnemy((int)Weight.NearEnemy / 3);

                    score += Evaluate_NexusBlood((int)Weight.NexusBlood);

                    score += Evaluate_NearTurret((int)Weight.NearTurret, 2);

                    score += Evaluate_ActionPoints((int)Weight.ActionPoints);

                    score += Evaluate_NearTheirTauber((int)Weight.NearTheirTauber);
                    break;

                case AIHeroData.AIMode.MaxDamage:
                    //雙方血量
                    //每消一點血，分數差距為(1 / 該英雄最大血量) * 100 * 權重 ~= 100~500
                    //4滴血的英雄打掉2滴是50分， 10滴的打掉1滴是10分，在乘上權重
                    score += Evaluate_HeroBlood((int)Weight.HeroBlood);

                    //場上英雄數量
                    //消滅一隻英雄，會使的分數多600分
                    score += Evaluate_HeroCount((int)Weight.HeroCount);

                    score += Evaluate_NearEnemy((int)Weight.NearEnemy);

                    score += Evaluate_NexusBlood((int)Weight.NexusBlood);

                    score += Evaluate_NearTurret((int)Weight.NearTurret, 2);

                    score += Evaluate_ActionPoints((int)Weight.ActionPoints);

                    score += Evaluate_NearTheirTauber((int)Weight.NearTheirTauber);
                    break;

                case AIHeroData.AIMode.Tank:
                    bc.AIUpdateHeroDistance(hero);

                    //如果脆皮英雄有在攻擊範圍內的話加分
                    //敵方共5之英雄，有1隻在攻擊範圍內，加1000分
                    int ChaseCrispy_Score = 0;
                    foreach (HeroHandler bhh in bHeroHandlerList) {
                        //0 - 無 1 - 鬥士 2 - 坦克 3 - 遠程 4 - 輔助
                        switch (bhh.heroDataItem.roleTag) {
                            case 3:
                            case 4:
                                //如果脆皮英雄在你的攻擊範圍內的話
                                if (ACanHitB(hero, bhh, SkillDistanceType.Enemy))
                                    ChaseCrispy_Score += (int)(1.0f / bHeroHandlerList.Count * 100);
                                break;
                            case 1:
                            case 2:
                                break;
                        }
                    }

                    if (ChaseCrispy_Score != 0) {
                        score += ChaseCrispy_Score * (int)Weight.ChaseCrispy;
                    } else //如果你附近沒有你攻擊的到的脆皮，那你應該鎖定新的脆皮、展開追擊
                      {
                        //我們不該讓這個分數反而比有脆皮在攻擊範圍高，所以先把他的初始分數下修，再從爛蘋果中挑一個比較不爛的
                        score += -BIGNUM;

                        HeroHandler tmphh = null;
                        int tmpRecoreder = BIGNUM;
                        foreach (HeroHandler bhh in bHeroHandlerList) {
                            //如果他是脆皮英雄，則我們挑一個最近的來追擊
                            if (bhh.heroDataItem.roleTag == 3 || bhh.heroDataItem.roleTag == 4) {
                                int distance = bc.boardHeroActionArrayXY[bhh.PositionXY[0], bhh.PositionXY[1], (int)BoardHeroActionArrayItem.Distance];
                                if (distance < tmpRecoreder && distance != 0) //尋找最近的脆皮英雄
                                {
                                    tmpRecoreder = distance;
                                    tmphh = bhh;
                                }
                            }
                        }
                        if (tmpRecoreder != BIGNUM) // 場上至少有一個脆皮英雄
                        {
                            score += 1.0f / tmpRecoreder * 1000000; //乘以一個很大的數字，讓他優先選擇靠近最近的脆皮
                        } else //場上都剩一堆胖子時，只好靠近他了， 總比在原地無所事事好
                          {
                            foreach (HeroHandler bhh in bHeroHandlerList) {
                                int distance = bc.boardHeroActionArrayXY[bhh.PositionXY[0], bhh.PositionXY[1], (int)BoardHeroActionArrayItem.Distance];
                                if (distance < tmpRecoreder && distance != 0) //尋找最近的英雄
                                {
                                    tmpRecoreder = distance;
                                    tmphh = bhh;
                                }
                            }
                            //[WARNING] 權重100支援最大距離為9，超過9距離還是不會追，因為分差小於1分了，會被捨去
                            score += 1.0f / tmpRecoreder * 100;
                        }
                    }

                    //別忘了順手打一下敵人，或是補一下隊友
                    //每消一點血，分數差距為(1 / 該英雄最大血量) * 100 * 權重 ~= 100~500
                    score += Evaluate_HeroBlood((int)Weight.HeroBlood * 10);

                    score += Evaluate_NexusBlood((int)Weight.NexusBlood);

                    score += Evaluate_NearTurret((int)Weight.NearTurret, 2);

                    score += Evaluate_NearTheirTauber((int)Weight.NearTheirTauber);
                    break;
            }
        }

        ec.evaluate += Time.realtimeSinceStartup - timer;
        return (int)score;
    }

    //雙方血量
    //每消一點血，分數差距為(1 / 該英雄最大血量) * 100 * 權重 ~= 
    // 4滴血的英雄打掉2滴是50分， 10滴的打掉1滴是10分，再乘上權重
    int Evaluate_HeroBlood(int weight) {
        float aScore = 0, bScore = 0;
        foreach (HeroHandler hh in aHeroHandlerList)
            //這個角色剩百分之幾的血量。意思是，最大血量越少的角色其每一滴血越值錢
            //注意資料型態，如果沒有轉成float，那角色只要不是在最大血量他的血量分數 = 0;
            //最後再乘上個別腳色的權重, 權重低的角色(ex: 小兵)的血量會顯得很不值錢
            aScore += (float)hh.NowHP * AIHeroData.dict[hh.heroId].protectWeight / hh.heroDataItem.hp * 100;
        foreach (HeroHandler hh in bHeroHandlerList)
            bScore += (float)hh.NowHP * AIHeroData.dict[hh.heroId].protectWeight / hh.heroDataItem.hp * 100;

        //差距越大，分數越高(我方血量越多的同時敵方血量越少)
        //int score = (int)((100 * (aScore - bScore)) / (aScore + bScore));

        int score = (int)(aScore - bScore);
        return score * weight;
    }

    //消滅或多存活一隻一隻英雄，會使的分數多1分
    int Evaluate_HeroCount(int weight) {
        //雙方英雄數量
        int aCount = 0, bCount = 0;
        //小兵被歸類為英雄的一種，但我們不把它列入計算範圍內
        foreach(HeroHandler ahh in aHeroHandlerList) {
            if (ahh.heroId != 2001)
                aCount++;
        }
        foreach(HeroHandler bhh in bHeroHandlerList) {
            if (bhh.heroId != 2001)
                bCount++;
        }

        int score = aCount - bCount;

        return score * weight;
    }

    //剩餘行動點數 (不會影響召喚師技能與上場的判斷)
    //每多一點行動點多1分
    int Evaluate_ActionPoints(int weight) {
        int score = 0;
        score = actionPoints;

        return actionPoints * weight;
    }

    //主堡血量
    //每消一點血，分數差距為(1 / 主堡最大血量) * 100 * 權重 ~= 
    // 最大血量10滴血的主堡打掉2滴是20分， 16滴的打掉2滴是12.5分，再乘上權重
    int Evaluate_NexusBlood(int weight) {

        float aScore = 0, bScore = 0;
        foreach (BuildingHandler abh in aBuildingHandlerList)
            aScore += (float)abh.NowHP / abh.buildingDataItem.hp * 100;
        foreach (BuildingHandler bbh in bBuildingHandlerList)
            bScore += (float)bbh.NowHP / bbh.buildingDataItem.hp * 100;

        int score = (int)(aScore - bScore);
        return score * weight;
    }

    //越多隻敵方英雄攻擊不到你，分數越高
    //分數為 有幾隻敵方英雄打不到你的比例 * 100, 對方有5隻英雄 3隻打不到你的艾希,分數為60分
    int Evaluate_CantBeHit(int weight, HeroHandler hero) {
        //同時也要盡可能的不要楚瑜在敵人的攻擊範圍內
        int CantBeHit_Score = 0;
        foreach (HeroHandler bhh in bHeroHandlerList)
            if (ACanHitBInOneMove(bhh, hero, SkillDistanceType.Enemy) == false) //如果敵人攻擊不到這支英雄(包括移動一格再打你)
                CantBeHit_Score += (int)(1.0f / bHeroHandlerList.Count * 100);
        int score = CantBeHit_Score;

        return score * weight;
    }

    int Evaluate_NearEnemy(int weight) {
        //此方與敵人英雄距離 (靠近彼方敵人加分)
        float NearEnemy_Score = 0;
        foreach (HeroHandler hh in aHeroHandlerList) {
            bc.AIUpdateHeroDistance(hh);
            foreach (HeroHandler ophh in bHeroHandlerList) {
                int distance = bc.boardHeroActionArrayXY[ophh.PositionXY[0], ophh.PositionXY[1], (int)BoardHeroActionArrayItem.Distance];
                NearEnemy_Score += 1.0f / distance * 100; //距離的倒數 * 100為分數。使用倒數的話， 分數在分母越小的時候變化越大
            }
        }
        int score = (int)NearEnemy_Score;
        
        return score * weight;
    }

    //靠近砲塔
    //每有一個英雄在離砲塔只有一格時，加1分，離砲塔兩格，加0.5分
    int Evaluate_NearTurret(int weight, int affectRange) {
        float nearScore = 0;
        foreach (BuildingHandler building in bc.p3BuildingHandlerList) {
            foreach (HeroHandler ahh in aHeroHandlerList) {
                int distance = GetChessboardDistance(new Position(ahh.PositionXY), new Position(building.PositionXY));
                if (distance <= affectRange) { //距離有在加分的計算範圍內
                    nearScore += 1.0f / distance; //使用距離的倒數, 距離越近分數越高
                }
            }
        }

        float score = nearScore;

        return (int)(score * weight);
    }

    //離對方主堡越近, 分數越高
    int Evaluate_NearTheirTauber(int weight, int affectRange=10) {
        int nearScore = 0;
        foreach (HeroHandler ahh in aHeroHandlerList) {
            foreach (BuildingHandler bbh in bBuildingHandlerList) {            
                int distance = GetChessboardDistance(new Position(ahh.PositionXY), new Position(bbh.PositionXY));
                if (distance < affectRange) {
                    nearScore += (affectRange - distance);
                }
            }
        }
        return nearScore * weight;
    }

    //我方英雄子力總合-敵方英雄子力總合(全算)(包含待命)
    float GetScore_HeroCount(float weight) {
        //我方
        float ourScore = 0;
        //上場的
        foreach (HeroHandler ahh in aHeroHandlerList) {
            ourScore += ((ahh.heroDataItem.seniorType == 2)?150:100) * ((float)ahh.nowHP+3) / ((float)ahh.maxHP+3);
        }
        //待命的
        foreach (int heroID in aWaitingListID) {
            ourScore += (heroID >= 500 && heroID < 900) ? 150 : 100;//TODO: 辨別黃金英雄應該有更好的寫法.. 
        }
        //主堡
        foreach (BuildingHandler building in aBuildingHandlerList) {
            if (building.nowHP > 0) {
                ourScore += 200 * ((float)building.nowHP + 3f) / ((float)building.buildingDataItem.hp+3f);
            }            
        }

        //對方
        float theirScore = 0;
        //上場的
        foreach (HeroHandler bhh in bHeroHandlerList) {
            theirScore += ((bhh.heroDataItem.seniorType == 2) ? 150 : 100) * ((float)bhh.nowHP + 3) / ((float)bhh.maxHP + 3);
        }
        //待命的
        foreach (int heroID in bWaitingListID) {
            theirScore += (heroID >= 500 && heroID < 900) ? 150 : 100;//TODO: 辨別黃金英雄應該有更好的寫法.. 
        }
        //主堡
        foreach (BuildingHandler building in bBuildingHandlerList) {
            if (building.nowHP > 0) {
                theirScore += 200 * ((float)building.nowHP + 3) / ((float)building.buildingDataItem.hp + 3);
            }            
        }

        return (ourScore - theirScore) * weight;
    }

    //對方主保距離x時, 加(10-x)分    
    float GetScore_TurretDistance(float weight) {
        //我方
        float ourScore = 0;
        foreach (BuildingHandler building in bBuildingHandlerList) {
            foreach (HeroHandler ahh in aHeroHandlerList) {
                int distance = GetChessboardDistance(new Position(ahh.PositionXY), new Position(building.PositionXY));
                if (25 - distance > 0) {
                    ourScore += (25 - distance);
                }                
            }
        }
        //敵方
        float theirScore = 0;
        foreach (BuildingHandler building in aBuildingHandlerList) {
            foreach (HeroHandler bhh in bHeroHandlerList) {
                int distance = GetChessboardDistance(new Position(bhh.PositionXY), new Position(building.PositionXY));
                if (25 - distance > 0) {
                    theirScore += (25 - distance);
                }
            }
        }
        return (ourScore - theirScore) * weight;
    }

    //安全分數, 被攻擊的程度越高, 扣分越多. 血多的tank可減少扣分
    float GetScore_BeAttacked(float weight) {
        //我方
        float ourScore = 0;
        foreach (HeroHandler ahh in aHeroHandlerList) {
            if (ahh.isSummon==1) {
                continue;//招喚物應該不用算
            }
            float nowHP = ahh.nowHP;
            float maxHP = ahh.maxHP;
            if (nowHP <= 0) {
                continue;
            }
            float beDamage = 0;
            //被對方英雄攻擊
            foreach (HeroHandler bhh in bHeroHandlerList) {
                if (ACanHitB(bhh, ahh, SkillDistanceType.Enemy)) {
                    //不移動可打到
                    beDamage += bhh.attack * bhh.skillDataItem.effectCount;
                } else if (ACanHitBInOneMove(bhh, ahh, SkillDistanceType.Enemy)) {
                    //移動一次可打到
                    //beDamage += bhh.attack * bhh.skillDataItem.effectCount/3f;
                }                
            }
            //被對方主堡攻擊
            foreach (BuildingHandler bh in bBuildingHandlerList) {
                if (IsInNexusAttackRange(new Position(bh.PositionXY), new Position(ahh.PositionXY)))
                    beDamage += bh.attack;
            }
            bool isGoldHero = (ahh.heroDataItem.seniorType == 2);
            if (beDamage < nowHP) {
                ourScore -= ((isGoldHero ? 150f : 100f) / 4f) * (nowHP + 3f) / (maxHP + 3f) * beDamage / maxHP * 6f / nowHP;
            } else {
                ourScore -= ((isGoldHero ? 150f : 100f) / 4f) * (nowHP + 3f) / (maxHP + 3f) * nowHP / maxHP * 6f / nowHP;
            }            
        }
        //敵方
        float theirScore = 0;
        // 避免與攻擊分數性值重複, 先不算
        return (ourScore-theirScore) * weight;  
    }

    //攻擊分數, 找給最大攻擊傷害的分數加分
    float GetScore_AttackAnyOne(float weight) {
        //我方
        float ourScore = 0;
        foreach (HeroHandler ahh in aHeroHandlerList) {
            //float myAtt = ahh.skillDataItem.targetDamage;
            float myAtt = ahh.attack * ahh.skillDataItem.effectCount;
            float maxScore = 0f;
            float heroScore = 0f;
            //打對方英雄
            foreach (HeroHandler bhh in bHeroHandlerList) {
                float enemyNowHP = bhh.nowHP;
                float enemyMaxHP = bhh.maxHP;
                bool isGoldHero = (bhh.heroDataItem.seniorType == 2);

                if (ACanHitB(ahh, bhh, SkillDistanceType.Enemy)) {
                    //不移動時
                    if (myAtt < enemyNowHP) {
                        heroScore = (isGoldHero ? 150f : 100f) / 3f * (enemyNowHP + 3f) / (enemyMaxHP + 3f) * myAtt / enemyNowHP;
                    } else {
                        heroScore = (isGoldHero ? 150f : 100f) / 3f * (enemyNowHP + 3f) / (enemyMaxHP + 3f);
                    }
                } else if (ACanHitBInOneMove(ahh, bhh, SkillDistanceType.Enemy)) {
                    //移動一步時
                    if (myAtt < enemyNowHP) {
                        heroScore = (isGoldHero ? 150f : 100f) / 9f * (enemyNowHP + 3f) / (enemyMaxHP + 3f) * myAtt / enemyNowHP;
                    } else {
                        heroScore = (isGoldHero ? 150f : 100f) / 9f * (enemyNowHP + 3f) / (enemyMaxHP + 3f);
                    }
                } else {
                    heroScore = 0;
                }

                if (heroScore > maxScore) {
                    maxScore = heroScore;
                }
            }
            //打對方主堡
            foreach (BuildingHandler bh in bBuildingHandlerList) {
                float enemyNowHP = bh.nowHP;
                float enemyMaxHP = bh.buildingDataItem.hp;
                if (ACanHitXY(ahh, bh.PositionXY, SkillDistanceType.Enemy)) {
                    //不移動時
                    if (myAtt < enemyNowHP) {
                        heroScore = (100f) / 3f * (enemyNowHP + 3f) / (enemyMaxHP + 3f) * myAtt / enemyNowHP;
                    } else {
                        heroScore = (100f) / 3f * (enemyNowHP + 3f) / (enemyMaxHP + 3f);
                    }
                } else if (ACanHitXYInOneMove(ahh, bh.PositionXY, SkillDistanceType.Enemy)) {
                    //移動一步時
                    if (myAtt < enemyNowHP) {
                        heroScore = (100f) / 9f * (enemyNowHP + 3f) / (enemyMaxHP + 3f) * myAtt / enemyNowHP;
                    } else {
                        heroScore = (100f) / 9f * (enemyNowHP + 3f) / (enemyMaxHP + 3f);
                    }
                } else {
                    heroScore = 0;
                }
                if (heroScore > maxScore) {
                    maxScore = heroScore;
                }
            }
            ourScore += maxScore;
        }
        //敵方
        float theirScore = 0;
        //避免與安全分數性質重複, 先不算
        return (ourScore - theirScore) * weight;
    }

    //配合分數, 太分散就扣分
    float GetScore_FriendAnyOne(float weight) {
        //我方
        float ourScore = 0;
        foreach (HeroHandler ahh in aHeroHandlerList) {
            //找與自己最近的我軍(含我方主堡)
            int minDistance = int.MaxValue;
            //我方英雄
            foreach (HeroHandler ahh_other in aHeroHandlerList) {
                if (ahh == ahh_other) {
                    continue;
                }
                int distance = GetChessboardDistance(new Position(ahh.PositionXY), new Position(ahh_other.PositionXY));
                if (distance < minDistance) {
                    minDistance = distance;
                }
                if (minDistance <= 2) {
                    break;
                }
            }
            //我方主堡
            foreach (BuildingHandler ah in aBuildingHandlerList) {
                int distance = GetChessboardDistance(new Position(ahh.PositionXY), new Position(ah.PositionXY));
                if (distance < minDistance) {
                    minDistance = distance;
                }
                if (minDistance <= 2) {
                    break;
                }
            }

            if (minDistance > 2) {
                ourScore -= 5;
            }
        }
        //敵方
        float theirScore = 0;
        foreach (HeroHandler bhh in bHeroHandlerList) {
            //找與自己最近的我軍(含我方主堡)
            int minDistance = int.MaxValue;
            //我方英雄
            foreach (HeroHandler bhh_other in bHeroHandlerList) {
                if (bhh == bhh_other) {
                    continue;
                }
                int distance = GetChessboardDistance(new Position(bhh.PositionXY), new Position(bhh_other.PositionXY));
                if (distance < minDistance) {
                    minDistance = distance;
                }
                if (minDistance <= 2) {
                    break;
                }
            }
            //我方主堡
            foreach (BuildingHandler bh in bBuildingHandlerList) {
                int distance = GetChessboardDistance(new Position(bhh.PositionXY), new Position(bh.PositionXY));
                if (distance < minDistance) {
                    minDistance = distance;
                }
                if (minDistance <= 2) {
                    break;
                }
            }

            if (minDistance > 2) {
                theirScore -= 5;
            }
        }

        return (ourScore-theirScore) * weight;
    }
#endregion

#region 搜尋、遍歷、產生可行的動作組合(Searching / Generate Aciton Sets)

#region OneRoundKill
    bool CanOneRoundKill() {
        bool canOneRoundKill = false;
        Dictionary<HeroHandler, int> beDamagedMax = new Dictionary<HeroHandler, int>();
        foreach (HeroHandler bhh in bHeroHandlerList) {
            if (!IsHeroAttackable(bhh)) {//不能打(如金鐘等)
                continue;
            }
            foreach (HeroHandler dhh in skipSearchHero) //已經搜尋過的，但現在還未殺死的英雄
                if (dhh == bhh) return false;

            beDamagedMax.Add(bhh, 0);
            foreach (HeroHandler ahh in aHeroHandlerList) {
                if (HeroCanSkill(ahh)) {
                    if (HeroCanMove(ahh)) {
                        //[WARNING]隊友擋住路沒有考慮，是個可能的BUG，你甚至可能被十面圍城(被其他英雄包住了)
                        if (ACanHitBInOneMove(ahh, bhh, SkillDistanceType.Enemy) && IsHeroAttackable(bhh))//有在攻擊範圍內(包括移動一步)
                        {
                            beDamagedMax[bhh] += ahh.attack * ahh.skillDataItem.effectCount;
                        } else //超出你的攻擊範圍， 需要依靠閃現、鬼步、隊友踢過來...等
                          {
                            ;
                            //閃現、鬼步? 讓誰使用?
                            //......

                            //移動敵人?
                            //......
                        }
                    } else //被冰凍...等時，可以考慮使用鬼步/閃現
                      {
                        if (ACanHitB(ahh, bhh, SkillDistanceType.Enemy) && IsHeroAttackable(bhh)) {
                            //不用動也打的到
                            beDamagedMax[bhh] += ahh.attack * ahh.skillDataItem.effectCount;
                        } else {
                            ;
                            //閃現、鬼步? 讓誰使用?
                            //......

                            //移動敵人?
                            //......
                        }
                    }
                } else //被暈眩、金人、冥想，或是已經沒有行動點數
                  {
                    ;
                    //目前沒有任何方法可以解除這些狀態，只能放棄
                }
            }
        }


        PlayerSkillHandler ignite = FindPlayerSkill(PlayerSkillID.Ignite);
        int maxWeight = -BIGNUM;
        foreach (KeyValuePair<HeroHandler, int> pair in beDamagedMax) {
            //這一回合中，我們能對這個腳色做的最大可能傷害
            int possibleMaxDamage = 0;
            int heroDamage = pair.Value;
            damageDelay = 0; // 晚點才會做出的傷害(如點燃、傳送)
            HeroHandler thisHero = pair.Key;

            if (ignite != null && ignite.nowCD == 1) damageDelay += ignite.playerSkillDataItem.damage; //點燃+1
            possibleMaxDamage = heroDamage + damageDelay;

            
            if (possibleMaxDamage >= thisHero.nowHP && AIHeroData.dict.ContainsKey(thisHero.heroId))//有機會殺得掉, 而且值得把招式灌在他身上
            {
                canOneRoundKill = true;
                if (AIHeroData.dict[thisHero.heroId].protectWeight >= maxWeight) //尋找最值得殺的hero，將他存入全域變數
                {
                    maxWeight = AIHeroData.dict[thisHero.heroId].protectWeight;
                    oneRoundKillHero = thisHero;
                }
            }
        }

        if (canOneRoundKill == false) oneRoundKillHero = null;
        return canOneRoundKill;
    }

    IEnumerator OneRoundKillSearch() {
        int rdIndex = bc.RecordData(); //backup all data in game, and get a index.
        List<Action> actionRecorder = new List<Action>();
        //先依據某方法排序 先跑不會移動的,後跑會移動的. 應該可減少人打飛搜尋失敗的狀況
        //TODO:先應急, 以後再改較正式的寫法        
        
        List<HeroHandler> sortHeroHandlerList = new List<HeroHandler>();
        //不會移動的先放
        foreach (HeroHandler ahh in aHeroHandlerList) {
            if (ahh.skillDataItem.targetDisplacementGoalType == 0) { 
                sortHeroHandlerList.Add(ahh);
            }            
        }
        //會移動的後放
        foreach (HeroHandler ahh in aHeroHandlerList) {
            if (ahh.skillDataItem.targetDisplacementGoalType != 0) {
                sortHeroHandlerList.Add(ahh);
            }
        }

        foreach (HeroHandler ahh in sortHeroHandlerList) {
            if (oneRoundKillHero == null || oneRoundKillHero.nowHP <= 0)
                break; //已經被消滅了

            bc.UpdateBoardHeroActionArrayXY(ActionType.HeroSkill, ahh.PositionXY[0], ahh.PositionXY[1]); //嘲諷添加
            if (HeroCanSkill(ahh)) {
                if (ACanHitB(ahh, oneRoundKillHero, SkillDistanceType.Enemy) && bc.boardHeroActionArrayXY[oneRoundKillHero.PositionXY[0], oneRoundKillHero.PositionXY[1], (int)BoardHeroActionArrayItem.BoardHeroAction] == (int)BoardHeroAction.CanSkill) {
                    Debug.Log(string.Format("OneRoundKill -> {0} :({1},{2}) Skill ({3},{4}, AttackDmg : {5})", ahh.heroDataItem.description, ahh.PositionXY[0], ahh.PositionXY[1], oneRoundKillHero.PositionXY[0], oneRoundKillHero.PositionXY[1], ahh.attack * ahh.skillDataItem.effectCount));
                    actionRecorder.Add(new Action(ActionType.HeroSkill, ahh.PositionXY, oneRoundKillHero.PositionXY));
                    yield return StartCoroutine(bc.DoHeroSkill(ahh, oneRoundKillHero.PositionXY, true));
                } else if (HeroCanMove(ahh) && ACanHitBInOneMove(ahh, oneRoundKillHero, SkillDistanceType.Enemy)) {
                    List<Position> moveList = FindHeroCanMovePosition(ahh);
                    int[] originPos = new int[2]; System.Array.Copy(ahh.PositionXY, originPos, ahh.PositionXY.Length);
                    foreach (Position pos in moveList) {
                        bc.DoHeroMove(originPos[0], originPos[1], pos.x, pos.y);
                        //[TODO] 打到就直接打了， 但其實應該要選擇最好的一個打位置
                        bc.UpdateBoardHeroActionArrayXY(ActionType.HeroSkill, ahh.PositionXY[0], ahh.PositionXY[1]);//嘲諷添加
                        if (ACanHitB(ahh, oneRoundKillHero, SkillDistanceType.Enemy) && bc.boardHeroActionArrayXY[oneRoundKillHero.PositionXY[0], oneRoundKillHero.PositionXY[1], (int)BoardHeroActionArrayItem.BoardHeroAction] == (int)BoardHeroAction.CanSkill) {
                            Debug.Log(string.Format("OneRoundKill -> {0} :({1},{2}) Move ({3},{4})", ahh.heroDataItem.description, originPos[0], originPos[1], pos.x, pos.y));
                            Debug.Log(string.Format("OneRoundKill -> {0} :({1},{2}) Skill ({3},{4}, AttackDmg : {5})", ahh.heroDataItem.description, ahh.PositionXY[0], ahh.PositionXY[1], oneRoundKillHero.PositionXY[0], oneRoundKillHero.PositionXY[1], ahh.attack * ahh.skillDataItem.effectCount));
                            actionRecorder.Add(new Action(ActionType.HeroMove, originPos, pos.ToArray()));
                            actionRecorder.Add(new Action(ActionType.HeroSkill, ahh.PositionXY, oneRoundKillHero.PositionXY));
                            yield return StartCoroutine(bc.DoHeroSkill(ahh, oneRoundKillHero.PositionXY, true));
                            break;
                        } else { bc.UndoHeroMove(pos.x, pos.y, originPos[0], originPos[1]); }
                    }
                }
            } else { continue; }
        }

        if (oneRoundKillHero.nowHP > damageDelay) //我們最後沒有成功擊殺那個英雄
        {
            //可能是 1.我們預估錯了， 其實無法擊殺他 2.我們在途中搞丟了目標(把他撞飛、他被其他什麼東西救了...等)
            //不論如何， 既然失敗了，我們放棄OneRoundKill，將盤面回復原始狀況， 之後再使用Traverse來動作。
            Debug.Log("OneRoundKill Failed");
            skipSearchHero.Add(oneRoundKillHero);
            bc.LoadRecordData(rdIndex, true);
        } else //有成功擊殺
          {
            actionToBeDone.AddRange(actionRecorder);
            skipSearchHero.Add(oneRoundKillHero);
        }
        yield break;
    }
#endregion

    IEnumerator TraverseEachHeroAction(List<HeroHandler> hh) {
        //for Efficiency Counter
        float timer = Time.realtimeSinceStartup;

        for (int i = 0; i < hh.Count; i++) {
            if (hh[i] == null) continue; //他可能被好運姊陰死了(被隊友殺死屬於例外)
            int rdIndex = bc.RecordData(); //backup all data in game, and get a index.

            List<Action[]> mActionSets = new List<Action[]>();

            //0.Do nothing
            mActionSets.Add(new Action[] { new Action(ActionType.DoNothing, new int[] { hh[i].PositionXY[0], hh[i].PositionXY[1] }) });

            //1.move then skill + 3.move but don't(or can't) skill
            if (HeroCanMove(hh[i])) {
                bc.UpdateBoardHeroActionArrayXY(ActionType.HeroMove, hh[i].PositionXY[0], hh[i].PositionXY[1]);
                int[,,] moveActionArray_backup = bc.boardHeroActionArrayXY;
                for (int mY = 0; mY < chessboardY; mY++)
                    for (int mX = 0; mX < chessboardX; mX++)
                        if (bc.boardHeroActionArrayXY[mX, mY, (int)BoardHeroActionArrayItem.BoardHeroAction] == (int)BoardHeroAction.CanMove) {
                            //this is (3) move but don't(or can't) skill
                            Action heroMove = new Action(ActionType.HeroMove,
                                new int[] { hh[i].PositionXY[0], hh[i].PositionXY[1] },
                                new int[] { mX, mY });
                            mActionSets.Add(new Action[] { heroMove });

                            //for (1).move then skill
                            if (HeroCanSkill(hh[i])) {
                                //move to (mX, mY)
                                bc.DoHeroMove(hh[i].PositionXY[0], hh[i].PositionXY[1], mX, mY);
                                bc.UpdateBoardHeroActionArrayXY(ActionType.HeroSkill, mX, mY);
                                for (int sY = 0; sY < chessboardY; sY++)
                                    for (int sX = 0; sX < chessboardX; sX++)
                                        if (bc.boardHeroActionArrayXY[sX, sY, (int)BoardHeroActionArrayItem.BoardHeroAction] == (int)BoardHeroAction.CanSkill)
                                            mActionSets.Add(new Action[] { heroMove, new Action(ActionType.HeroSkill,
                                                new int[] {mX, mY }, new int[] {sX, sY }) });
                                bc.UndoHeroMove(mX, mY, heroMove.userPos[0], heroMove.userPos[1]);
                                bc.boardHeroActionArrayXY = moveActionArray_backup;
                            }
                        }
            }
            //2.skill then move + 4.skill but don't(or can't) move
            if (HeroCanSkill(hh[i])) {
                bc.UpdateBoardHeroActionArrayXY(ActionType.HeroSkill, hh[i].PositionXY[0], hh[i].PositionXY[1]);
                int[,,] skillActionArray_backup = bc.boardHeroActionArrayXY;
                for (int sY = 0; sY < chessboardY; sY++)
                    for (int sX = 0; sX < chessboardX; sX++)
                        if (bc.boardHeroActionArrayXY[sX, sY, (int)BoardHeroActionArrayItem.BoardHeroAction] == (int)BoardHeroAction.CanSkill) {
                            //this is (4).skill but don't(or can't) move
                            Action heroSkill = new Action(ActionType.HeroSkill,
                                new int[2] { hh[i].PositionXY[0], hh[i].PositionXY[1] }, new int[2] { sX, sY });
                            mActionSets.Add(new Action[] { heroSkill });

                            //for (2).skill then move
                            if (HeroCanMove(hh[i])) {
                                //bc.lockHeroHandler = aHeroHandlerList[i];
                                //bc.DoHeroSkill(sX, sY, true);
                                yield return StartCoroutine(bc.DoHeroSkill(hh[i], new int[] { sX, sY }, true));
                                bc.UpdateBoardHeroActionArrayXY(ActionType.HeroMove, hh[i].PositionXY[0], hh[i].PositionXY[1]);
                                for (int mY = 0; mY < chessboardY; mY++)
                                    for (int mX = 0; mX < chessboardX; mX++)
                                        if (bc.boardHeroActionArrayXY[mX, mY, (int)BoardHeroActionArrayItem.BoardHeroAction] == (int)BoardHeroAction.CanMove) {
                                            mActionSets.Add(new Action[] {heroSkill, new Action(ActionType.HeroMove,
                                            new int[] {hh[i].PositionXY[0], hh[i].PositionXY[1] },
                                            new int[] {mX, mY }) });
                                        }
                                bc.LoadRecordData_Hero(rdIndex);
                                bc.boardHeroActionArrayXY = skillActionArray_backup;
                            }
                        }
            }

            //到這裡我們已經找完這個英雄在目前盤面的所有動作組合
            //繞行每一個動作組合，評估這個動作是否是好的，然後進到下一個英雄進行搜尋
            Action[] bestAction = new Action[] { new Action(ActionType.DoNothing) };
            int maxScore = -BIGNUM * 2; //Evaluate Tank的寫法有點詭異 所以這邊才要*2
            foreach (Action[] aa in mActionSets) {
                yield return StartCoroutine(AIFakeMove(aa));
                int score = Evaluate((AIHeroData.AIMode)AIHeroData.dict[hh[i].heroId].aiHeroMode, hh[i]);

                if (score >= maxScore) {
                    maxScore = score;
                    bestAction = aa;
                }
                bc.LoadRecordData_Hero(rdIndex);

                nodeCounter++;

#if UNITY_EDITOR && DEBUG && AI_DEBUG
                //FOR DEBUG
                HeroHandler hero = bc.chessboardYX[aa[0].userPos[1], aa[0].userPos[0], (int)BattleController.ChessboardItem.Unit].GetComponent<HeroHandler>();
                if (aa[0].type != ActionType.DoNothing) {
                    if (aa.Length == 2) {
                        if (aa[0].type == ActionType.HeroMove) {
                            Debug.Log(string.Format("{0} :({1},{2}) Move ({3},{4}) && ({5},{6}) Skill ({7},{8}). Score : {9}", hero.heroDataItem.description, aa[0].userPos[0], aa[0].userPos[1], aa[0].targetPos[0], aa[0].targetPos[1], aa[1].userPos[0], aa[1].userPos[1], aa[1].targetPos[0], aa[1].targetPos[1], score));
                        } else if (aa[0].type == ActionType.HeroSkill) {
                            Debug.Log(string.Format("{0} :({1},{2}) Skill ({3},{4}) && ({5},{6}) Move ({7},{8}). Score : {9}", hero.heroDataItem.description, aa[0].userPos[0], aa[0].userPos[1], aa[0].targetPos[0], aa[0].targetPos[1], aa[1].userPos[0], aa[1].userPos[1], aa[1].targetPos[0], aa[1].targetPos[1], score));
                        }
                    } else if (aa.Length == 1) {
                        if (aa[0].type == ActionType.HeroMove)
                            Debug.Log(string.Format("{0} :({1},{2}) Move ({3},{4}). Score : {5}", hero.heroDataItem.description, aa[0].userPos[0], aa[0].userPos[1], aa[0].targetPos[0], aa[0].targetPos[1], score));
                        else if (aa[0].type == ActionType.HeroSkill)
                            Debug.Log(string.Format("{0} :({1},{2}) Skill ({3},{4}). Score : {5}", hero.heroDataItem.description, aa[0].userPos[0], aa[0].userPos[1], aa[0].targetPos[0], aa[0].targetPos[1], score));
                    } else {
                        Debug.LogError("shit...");
                    }
                } else {
                    Debug.Log(string.Format("{0} :DO NOTHING. Score : {1}", hero.heroDataItem.description, score));
                }
#endif
            }


            foreach (Action a in bestAction) //記錄走過的動作。
                actionToBeDone.Add(a);
            bc.ClearRecordLastData(); //清除不會再用到的recordData，減少記憶體浪費

#if UNITY_EDITOR && DEBUG && AI_DEBUG
            //FOR DEBUG
            HeroHandler bestHero = bc.chessboardYX[bestAction[0].userPos[1], bestAction[0].userPos[0], (int)BattleController.ChessboardItem.Unit].GetComponent<HeroHandler>();
            if (bestAction[0].type != ActionType.DoNothing) {
                if (bestAction.Length == 2) {
                    if (bestAction[0].type == ActionType.HeroMove) {
                        Debug.Log(string.Format("best for : {0} :({1},{2}) Move ({3},{4}) && ({5},{6}) Skill ({7},{8}). Score : {9}", bestHero.heroDataItem.description, bestAction[0].userPos[0], bestAction[0].userPos[1], bestAction[0].targetPos[0], bestAction[0].targetPos[1], bestAction[1].userPos[0], bestAction[1].userPos[1], bestAction[1].targetPos[0], bestAction[1].targetPos[1], maxScore));
                    } else if (bestAction[0].type == ActionType.HeroSkill) {
                        Debug.Log(string.Format("best for : {0} :({1},{2}) Skill ({3},{4}) && ({5},{6}) Move ({7},{8}). Score : {9}", bestHero.heroDataItem.description, bestAction[0].userPos[0], bestAction[0].userPos[1], bestAction[0].targetPos[0], bestAction[0].targetPos[1], bestAction[1].userPos[0], bestAction[1].userPos[1], bestAction[1].targetPos[0], bestAction[1].targetPos[1], maxScore));
                    }
                } else if (bestAction.Length == 1) {
                    if (bestAction[0].type == ActionType.HeroMove)
                        Debug.Log(string.Format("best for : {0} :({1},{2}) Move ({3},{4}). Score : {5}", bestHero.heroDataItem.description, bestAction[0].userPos[0], bestAction[0].userPos[1], bestAction[0].targetPos[0], bestAction[0].targetPos[1], maxScore));
                    else if (bestAction[0].type == ActionType.HeroSkill)
                        Debug.Log(string.Format("best for : {0} :({1},{2}) Skill ({3},{4}). Score : {5}", bestHero.heroDataItem.description, bestAction[0].userPos[0], bestAction[0].userPos[1], bestAction[0].targetPos[0], bestAction[0].targetPos[1], maxScore));
                } else {
                    Debug.LogError("shit...");
                }
            } else {
                Debug.Log(string.Format("best for : {0} :DO NOTHING. Score : {1}", bestHero.heroDataItem.description, maxScore));
            }
#endif
            //在進入下個英雄前， 先把剛剛找到的的最佳動作實作
            yield return StartCoroutine(AIFakeMove(bestAction));
        }

        ec.traverse += Time.realtimeSinceStartup - timer;
        yield break;
    }
#region 召喚師技能相關搜尋(PlayerSkill related)
    IEnumerator Before_PlayerSkillSearch() {
        yield break;
    }

    IEnumerator After_PlayerSkillSearch() {
        //for Efficiency Counter
        float timer = Time.realtimeSinceStartup;

        int backupIndex = bc.RecordData();

        after_playerSkillToBeDone = new List<Action>();

        List<Action> tmpPSkillRecorder = new List<Action>();

        PlayerSkillHandler teleport = FindPlayerSkill(PlayerSkillID.Teleport);
        PlayerSkillHandler ignite = FindPlayerSkill(PlayerSkillID.Ignite);
        PlayerSkillHandler heal = FindPlayerSkill(PlayerSkillID.Heal);
        PlayerSkillHandler gold = FindPlayerSkill(PlayerSkillID.Gold);

        //使用順位 傳送 >> 點燃 >> 治癒 >> 金人
        if (teleport != null && teleport.isReady && bc.ThisRoundCanAddHeroCount > 0 && actionPoints >= 4) //傳送
        {
            int maxDamage = 0;
            foreach (int waitHeroId in aUnLockedWaitListID) {
                int tmp = GetTargetDamageByHeroId(waitHeroId);
                if (tmp >= maxDamage) maxDamage = tmp;
            }
            if (ignite != null && ignite.isReady && actionPoints >= 6) maxDamage += ignite.playerSkillDataItem.damage; //[TODO] 其實5點行動點就有可能搭配點燃使用,但害怕他上3點行動點的位置導致點燃沒行動點使用

            int maxWeight = 0;
            HeroHandler killTarget = null;
            bool usedIgnite = false; //點燃是否已經被分配給一個英雄使用了
            foreach (HeroHandler bhh in bHeroHandlerList) {
                if (IsHeroAttackable(bhh) == false) break;
                if (ignite != null && ignite.isReady && bhh.nowHP <= ignite.playerSkillDataItem.damage && !usedIgnite) {
                    usedIgnite = true;
                    continue; //如果敵方英雄的血量低於點燃且點燃可以使用， 則略過他，待會再用點燃擊殺他即可
                }
                if (bhh.nowHP <= maxDamage && AIHeroData.dict[bhh.heroId].protectWeight >= maxWeight) {
                    if (actionPoints >= 5) {
                        maxWeight = AIHeroData.dict[bhh.heroId].protectWeight;
                        killTarget = bhh;
                    } else {//行動點不足以上在3的位置
                        for(int i = 0; i < 6; i++) {
                            Position targetHeroPos = new Position(bhh.PositionXY);
                            Position offsetPos = sixSides[i];
                            Position possibleCallPos = new Position(targetHeroPos + offsetPos);
                            if (ActionPointToXY(possibleCallPos) == 2) {
                                maxWeight = AIHeroData.dict[bhh.heroId].protectWeight;
                                killTarget = bhh;
                            } else {
                                continue; //放棄
                            }
                        }
                    }
                }
            }

            if (killTarget != null) {
                bc.UsePlayerSkill(teleport, bc.nowGameTeam);

                //選那個要被上場的英雄
                int heroIdToBeCalled = 0;
                for (int i = 0; i < aWaitingListID.Count; i++) {
                    int aIndex = i % aWaitingListID.Count;
                    if (aWaitingListID.Count < bc.canSeeWaitingHeroCount) aIndex = i % bc.canSeeWaitingHeroCount;
                    int aHeroDesignerID = aWaitingListID[aIndex];
                    heroIdToBeCalled = aHeroDesignerID;
                    int heroDamage = GetTargetDamageByHeroId(aHeroDesignerID);
                    int igniteDamage = ignite == null || !ignite.isReady ? 0 : ignite.playerSkillDataItem.damage;
                    int damageSum = heroDamage + igniteDamage;
                    if (damageSum >= killTarget.nowHP && IsHeroCanBeCalled(aHeroDesignerID)) {
                        bc.lockID = i;
                        if (bc.nowGameTeam == GameTeam.A) bc.p1WaitingImageListIndex = i;
                        else bc.p2WaitingImageListIndex = i;
                        break;
                    }
                }

                SetCursorPosition(NowTeamWaitingCursorPos);
                PressEnter();

                //要上到哪個位置呢? (先上旁邊)
                int aCenterX = killTarget.PositionXY[0], aCenterY = killTarget.PositionXY[1];
                int targetX = -1, targetY = -1;
                for (int distance = 1; distance <= 1; ++distance)
                    for (int adjust = 0; adjust < distance; ++adjust) {
                        if (bc.IsXYCanAddHero(aCenterX - adjust, aCenterY - distance)) {
                            targetX = aCenterX - adjust; targetY = aCenterY - distance;
                        }
                        if (bc.IsXYCanAddHero(aCenterX + distance - adjust, aCenterY - adjust)) {
                            targetX = aCenterX + distance - adjust; targetY = aCenterY - adjust;
                        }
                        if (bc.IsXYCanAddHero(aCenterX + distance, aCenterY + distance - adjust)) {
                            targetX = aCenterX + distance; targetY = aCenterY + distance - adjust;
                        }
                        if (bc.IsXYCanAddHero(aCenterX + adjust, aCenterY + distance)) {
                            targetX = aCenterX + adjust; targetY = aCenterY + distance;
                        }
                        if (bc.IsXYCanAddHero(aCenterX - distance + adjust, aCenterY + adjust)) {
                            targetX = aCenterX - distance + adjust; targetY = aCenterY + adjust;
                        }
                        if (bc.IsXYCanAddHero(aCenterX - distance, aCenterY - distance + adjust)) {
                            targetX = aCenterX - distance; targetY = aCenterY - distance + adjust;
                        }
                    }

                Debug.Log(string.Format("{0} : [{1}]({2},{3}) attack ({4},{5})", teleport.playerSkillDataItem.name, HeroData.ItemDict[heroIdToBeCalled].description, targetX, targetY, killTarget.PositionXY[0], killTarget.PositionXY[1]));

                if (targetX != -1 && targetY != -1) {
                    SetCursorPosition(CursorPosition.Chessboard);
                    bc.nowCursorX = targetX; bc.nowCursorY = targetY;
                    yield return StartCoroutine(bc.DoPlayerSkill_DoubleLock(true));
                    HeroHandler dropHero = bc.chessboardYX[targetY, targetX, (int)BattleController.ChessboardItem.Unit].GetComponent<HeroHandler>();
                    yield return StartCoroutine(bc.DoHeroSkill(dropHero, killTarget.PositionXY, true));
                    Action action_Teleport = new Action(ActionType.UseSummonerSkill, new int[] { teleport.playerSkillID, heroIdToBeCalled }, killTarget.PositionXY);
                    teleportPos = new int[2] { targetX, targetY };
                    tmpPSkillRecorder.Add(action_Teleport);
                } else //他附近沒有可以上的位置
                  {
                    bc.NowLockType = BattleController.LockType.None;
                    SetCursorPosition(CursorPosition.None);
                    bc.LoadRecordData(backupIndex, true);
                }
            }
        }

        if (ignite != null && ignite.isReady && actionPoints >= 1) //點燃
        {
            HeroHandler tmphh_ignite = null;
            int maxScore = 3; //價值大於三的才點燃，避免點約的小鬼這類的王八
            foreach (HeroHandler hh in bHeroHandlerList) {
                if (hh.NowHP <= ignite.playerSkillDataItem.damage && IsHeroAttackable(hh)) //點燃HP剩下1的敵方角色，而且他身上不能有金人冥想等等效果
                {
                    if (AIHeroData.dict[hh.heroId].protectWeight > maxScore) {
                        maxScore = AIHeroData.dict[hh.heroId].protectWeight;
                        tmphh_ignite = hh;
                    }
                }
            }
            if (tmphh_ignite != null) {
                Action action_Ignite = new Action(ActionType.UseSummonerSkill, new int[] { ignite.playerSkillID }, tmphh_ignite.PositionXY);
                tmpPSkillRecorder.Add(action_Ignite);

                Debug_PlayerSkill(AIHeroData.dict[tmphh_ignite.heroId].protectWeight, ignite, tmphh_ignite);

                bc.UsePlayerSkill(ignite, bc.nowGameTeam);
                bc.nowCursorX = tmphh_ignite.PositionXY[0]; bc.nowCursorY = tmphh_ignite.PositionXY[1];
                yield return StartCoroutine(bc.DoPlayerSkill(true));
            }
        }

        if (heal != null && heal.isReady && actionPoints >= 1) //治癒 (ps.必須在金人之前)
        {
            //角色有價值，而且血量低於(總血量 - 治癒的補血量)即可使用治癒，不用想太多
            int healAmount = bc.gameData.playerSkills.getPlayerSkillData((int)PlayerSkillID.Heal).heal;
            int tmpScore_heal = -BIGNUM;
            HeroHandler tmphh_heal = null;
            foreach (HeroHandler hh in aHeroHandlerList) {
                if (hh.NowHP <= hh.heroDataItem.hp - healAmount && IsHeroHealable(hh))
                    if (AIHeroData.dict[hh.heroId].protectWeight >= 20) //角色價值大於20才有治癒的價值
                        if (AIHeroData.dict[hh.heroId].protectWeight > tmpScore_heal) {
                            tmpScore_heal = AIHeroData.dict[hh.heroId].protectWeight;
                            tmphh_heal = hh;
                        }
            }
            if (tmphh_heal != null) {
                Action action_heal = new Action(ActionType.UseSummonerSkill, new int[] { heal.playerSkillID }, tmphh_heal.PositionXY);
                tmpPSkillRecorder.Add(action_heal);

                Debug_PlayerSkill(tmpScore_heal, heal, tmphh_heal);

                bc.UsePlayerSkill(heal, bc.nowGameTeam);
                bc.nowCursorX = tmphh_heal.PositionXY[0]; bc.nowCursorY = tmphh_heal.PositionXY[1];
                yield return StartCoroutine(bc.DoPlayerSkill(true));
            }
        }

        if (gold != null && gold.isReady && actionPoints >= 1) //金人
        {
            Dictionary<HeroHandler, int> score = new Dictionary<HeroHandler, int>();
            //下一回合最大可能受到的傷害(沒有考慮 1.被點燃 2.對方用傳送、閃現、或鬼步 3.被動的影響 4.炸彈)
            HeroHandler tmphh_gold = null;
            int tmpScore = -BIGNUM;
            foreach (HeroHandler ahh in aHeroHandlerList) {
                int expectBeDamage = 0;
                foreach (HeroHandler bhh in bHeroHandlerList) {
                    if (ACanHitBInOneMove(bhh, ahh, SkillDistanceType.Enemy))
                        expectBeDamage += bhh.skillDataItem.targetDamage;
                }
                //如果這支英雄下一回合有機會被集火致死，而且他有值得被救的價值(權重>=50)的話
                if (expectBeDamage >= ahh.NowHP && AIHeroData.dict[ahh.heroId].protectWeight >= 50)
                    score.Add(ahh, AIHeroData.dict[ahh.heroId].protectWeight); //根據權重決定分數，分數決定他會不會優先被救
                if (score.ContainsKey(ahh) && score[ahh] >= tmpScore) {
                    tmpScore = score[ahh];
                    tmphh_gold = ahh;
                }
            }

            if (tmphh_gold != null) //如果有找到需要金人的角色
            {
                Action action_gold = new Action(ActionType.UseSummonerSkill, new int[] { gold.playerSkillID }, tmphh_gold.PositionXY);
                tmpPSkillRecorder.Add(action_gold);

                Debug_PlayerSkill(tmpScore, gold, tmphh_gold);

                bc.UsePlayerSkill(gold, bc.nowGameTeam);
                bc.nowCursorX = tmphh_gold.PositionXY[0]; bc.nowCursorY = tmphh_gold.PositionXY[1];
                yield return StartCoroutine(bc.DoPlayerSkill(true));
            }
        }

        //使用順位 傳送 >> 點燃 >> 治癒 >> 金人  (找不到(沒有使用)會回傳一個userPos -1的 Action(代表啥都不做))
        //Action doNothingAction = new Action(ActionType.DoNothing);
        after_playerSkillToBeDone.Add(tmpPSkillRecorder.Find(x => x.userPos[0] == (int)PlayerSkillID.Teleport)); //傳送
        after_playerSkillToBeDone.Add(tmpPSkillRecorder.Find(x => x.userPos[0] == (int)PlayerSkillID.Ignite)); //點燃
        after_playerSkillToBeDone.Add(tmpPSkillRecorder.Find(x => x.userPos[0] == (int)PlayerSkillID.Heal)); //治癒
        after_playerSkillToBeDone.Add(tmpPSkillRecorder.Find(x => x.userPos[0] == (int)PlayerSkillID.Gold)); //金人

        ec.playerskill += Time.realtimeSinceStartup - timer;
        yield break;
    }
#endregion

    void WaitingHeroSearching() {
        //for Efficiency Counter
        float timer = Time.realtimeSinceStartup;

        if (bc.ThisRoundCanAddHeroCount < 1 || aWaitingListID.Count == 0)
            return;

        //Score boards        
        int[,] expectBeDamage;
        int[,] nearTeammate = new int[chessboardX, chessboardY];
        int[,] nearEnemy = new int[chessboardX, chessboardY];
        int[,] nearTurret = new int[chessboardY, chessboardY];

        int nearTeamAffectRange = 4; //在幾個圓的範圍內有隊友會加分?
        int nearEnemyAffectRange = 4;

        //在每個格子降落各會受到多少傷害 ? (包括被動傷害如凱特琳)
        expectBeDamage = expectBeDamaged_CallToChessboard();

        //在附近友軍數量多的地方降落通常是個不錯的選擇
        foreach (HeroHandler hh in aHeroHandlerList) {
            int aCenterX = hh.PositionXY[0], aCenterY = hh.PositionXY[1];
            for (int distance = 1; distance <= nearTeamAffectRange; ++distance)
                for (int adjust = 0; adjust < distance; ++adjust) {
                    //分數 = 距離的倒數 * 100 (ex: 距離隊友2(中間隔一格)則分數為 1/2 * 100, 距離3為 1/3 * 100)
                    if (bc.IsXYCanAddHero(aCenterX - adjust, aCenterY - distance))
                        nearTeammate[aCenterX - adjust, aCenterY - distance] += 1 / distance * 100;
                    if (bc.IsXYCanAddHero(aCenterX + distance - adjust, aCenterY - adjust))
                        nearTeammate[aCenterX + distance - adjust, aCenterY - adjust] += 1 / distance * 100;
                    if (bc.IsXYCanAddHero(aCenterX + distance, aCenterY + distance - adjust))
                        nearTeammate[aCenterX + distance, aCenterY + distance - adjust] += 1 / distance * 100;
                    if (bc.IsXYCanAddHero(aCenterX + adjust, aCenterY + distance))
                        nearTeammate[aCenterX + adjust, aCenterY + distance] += 1 / distance * 100;
                    if (bc.IsXYCanAddHero(aCenterX - distance + adjust, aCenterY + adjust))
                        nearTeammate[aCenterX - distance + adjust, aCenterY + adjust] += 1 / distance * 100;
                    if (bc.IsXYCanAddHero(aCenterX - distance, aCenterY - distance + adjust))
                        nearTeammate[aCenterX - distance, aCenterY - distance + adjust] += 1 / distance * 100;
                }
        }

        //靠近敵人
        foreach (HeroHandler hh in bHeroHandlerList) {
            int aCenterX = hh.PositionXY[0], aCenterY = hh.PositionXY[1];
            for (int distance = 1; distance <= nearEnemyAffectRange; ++distance)
                for (int adjust = 0; adjust < distance; ++adjust) {
                    //分數 = 距離的倒數 * 100 (ex: 距離敵人2(中間隔一格)則分數為 1/2 * 100, 距離3為 1/3 * 100)
                    if (bc.IsXYCanAddHero(aCenterX - adjust, aCenterY - distance))
                        nearEnemy[aCenterX - adjust, aCenterY - distance] += 1 / distance * 100;
                    if (bc.IsXYCanAddHero(aCenterX + distance - adjust, aCenterY - adjust))
                        nearEnemy[aCenterX + distance - adjust, aCenterY - adjust] += 1 / distance * 100;
                    if (bc.IsXYCanAddHero(aCenterX + distance, aCenterY + distance - adjust))
                        nearEnemy[aCenterX + distance, aCenterY + distance - adjust] += 1 / distance * 100;
                    if (bc.IsXYCanAddHero(aCenterX + adjust, aCenterY + distance))
                        nearEnemy[aCenterX + adjust, aCenterY + distance] += 1 / distance * 100;
                    if (bc.IsXYCanAddHero(aCenterX - distance + adjust, aCenterY + adjust))
                        nearEnemy[aCenterX - distance + adjust, aCenterY + adjust] += 1 / distance * 100;
                    if (bc.IsXYCanAddHero(aCenterX - distance, aCenterY - distance + adjust))
                        nearEnemy[aCenterX - distance, aCenterY - distance + adjust] += 1 / distance * 100;
                }
        }

        //靠近砲塔
        foreach (BuildingHandler bh in bc.p3BuildingHandlerList) {
            int aCenterX = bh.PositionXY[0], aCenterY = bh.PositionXY[1];
            for (int distance = 1; distance <= 1; ++distance)
                for (int adjust = 0; adjust < distance; ++adjust) {
                    //分數 = 距離的倒數 * 100 (ex: 距離敵人2(中間隔一格)則分數為 1/2 * 100, 距離3為 1/3 * 100)
                    if (bc.IsXYCanAddHero(aCenterX - adjust, aCenterY - distance))
                        nearTurret[aCenterX - adjust, aCenterY - distance] += 1 / distance * 100;
                    if (bc.IsXYCanAddHero(aCenterX + distance - adjust, aCenterY - adjust))
                        nearTurret[aCenterX + distance - adjust, aCenterY - adjust] += 1 / distance * 100;
                    if (bc.IsXYCanAddHero(aCenterX + distance, aCenterY + distance - adjust))
                        nearTurret[aCenterX + distance, aCenterY + distance - adjust] += 1 / distance * 100;
                    if (bc.IsXYCanAddHero(aCenterX + adjust, aCenterY + distance))
                        nearTurret[aCenterX + adjust, aCenterY + distance] += 1 / distance * 100;
                    if (bc.IsXYCanAddHero(aCenterX - distance + adjust, aCenterY + adjust))
                        nearTurret[aCenterX - distance + adjust, aCenterY + adjust] += 1 / distance * 100;
                    if (bc.IsXYCanAddHero(aCenterX - distance, aCenterY - distance + adjust))
                        nearTurret[aCenterX - distance, aCenterY - distance + adjust] += 1 / distance * 100;
                }
        }

        List<int> waitingListOrder = new List<int>();

        //如果有安全位置，就先上射手; 如果都不安全，就根據戰場上缺哪種腳色補哪種
        //尋找最安全的格子
        int minDamage = BIGNUM;
        for (int y = 0; y < chessboardY; y++)
            for (int x = 0; x < chessboardX; x++) {
                if (expectBeDamage[x, y] < minDamage && bc.IsXYCanAddHero(x, y)) {
                    minDamage = expectBeDamage[x, y];
                }
            }

        for (int i = 0; i < aUnLockedWaitListID.Count; i++)
            if ((AIHeroData.AIMode)AIHeroData.dict[aUnLockedWaitListID[i]].aiHeroMode == AIHeroData.AIMode.MaxAttackDistance)//遠程
            {
                if (HeroData.ItemDict[aUnLockedWaitListID[i]].hp / 2 > minDamage && IsDominatingOnNowChessboard()) //如果有不會一上場就被打掉半條血的位置的話
                    waitingListOrder.Add(i);
            }

        //計算現在場上每個職業的數量，尋找數量最少的補上
        int roleTagCount = 4;
        int[] roleCount = new int[roleTagCount];
        foreach (HeroHandler hh in aHeroHandlerList)
            roleCount[hh.heroDataItem.roleTag - 1]++;
        int minRoleValue = roleCount.Min();
        int minRoleIndex = roleCount.ToList().IndexOf(minRoleValue);

        for (int i = 0; i < aUnLockedWaitListID.Count; i++)
            if (AIHeroData.dict[aUnLockedWaitListID[i]].aiHeroMode == minRoleIndex + 1) //index starts at 0, so plus one
                waitingListOrder.Add(i);


        //先上遠程，再上傷害、再上輔助(功能角)，最後坦克、平衡
        //但如果盤面逆風的話 先上坦克為主
        for (int i = 0; i < aUnLockedWaitListID.Count; i++)
            if ((AIHeroData.AIMode)AIHeroData.dict[aUnLockedWaitListID[i]].aiHeroMode == AIHeroData.AIMode.MaxAttackDistance && IsDominatingOnNowChessboard())
                waitingListOrder.Add(i);
        for (int i = 0; i < aUnLockedWaitListID.Count; i++)
            if ((AIHeroData.AIMode)AIHeroData.dict[aUnLockedWaitListID[i]].aiHeroMode == AIHeroData.AIMode.MaxDamage && IsDominatingOnNowChessboard())
                waitingListOrder.Add(i);
        for (int i = 0; i < aUnLockedWaitListID.Count; i++)
            if ((AIHeroData.AIMode)AIHeroData.dict[aUnLockedWaitListID[i]].aiHeroMode == AIHeroData.AIMode.Escape && IsDominatingOnNowChessboard())
                waitingListOrder.Add(i);
        for (int i = 0; i < aUnLockedWaitListID.Count; i++)
            if ((AIHeroData.AIMode)AIHeroData.dict[aUnLockedWaitListID[i]].aiHeroMode == AIHeroData.AIMode.Tank)
                waitingListOrder.Add(i);

        //避免他牌組沒有坦克，而現在又是逆風 導致沒人可上
        for (int i = 0; i < aUnLockedWaitListID.Count; i++)
            if ((AIHeroData.AIMode)AIHeroData.dict[aUnLockedWaitListID[i]].aiHeroMode == AIHeroData.AIMode.MaxDamage)
                waitingListOrder.Add(i);
        for (int i = 0; i < aUnLockedWaitListID.Count; i++)
            if ((AIHeroData.AIMode)AIHeroData.dict[aUnLockedWaitListID[i]].aiHeroMode == AIHeroData.AIMode.MaxAttackDistance)
                waitingListOrder.Add(i);
        for (int i = 0; i < aUnLockedWaitListID.Count; i++)
            if ((AIHeroData.AIMode)AIHeroData.dict[aUnLockedWaitListID[i]].aiHeroMode == AIHeroData.AIMode.Escape)
                waitingListOrder.Add(i);
        for (int i = 0; i < aUnLockedWaitListID.Count; i++)
            if ((AIHeroData.AIMode)AIHeroData.dict[aUnLockedWaitListID[i]].aiHeroMode == AIHeroData.AIMode.Balance)
                waitingListOrder.Add(i);



        //為每個等待區的英雄計算它的板凳分數，以及他的最佳上場位置
        Dictionary<int, int> bestScoreDict = new Dictionary<int, int>();
        Dictionary<int, int[]> bestPosXYDict = new Dictionary<int, int[]>();
        bc.UpdateBoardHeroActionArrayXY(ActionType.CallWaitingHero);
        for (int i = 0; i < aUnLockedWaitListID.Count; i++) {
            if (bestScoreDict.ContainsKey(aUnLockedWaitListID[i])) continue; //有同樣id的英雄， 那他們Search的結果也必然相同，直接跳過才不會有bug

            int[,] finalScore = new int[chessboardX, chessboardY]; //reset scoreboard for each hero in waiting list
            int tmpScore = -BIGNUM, tmpPosX = -1, tmpPosY = -1;
            for (int y = 0; y < chessboardY; y++)
                for (int x = 0; x < chessboardX; x++)
                    if (bc.boardHeroActionArrayXY[x, y, (int)BoardHeroActionArrayItem.BoardHeroAction] == (int)BoardHeroAction.CanAddWaitingHero) {
                        if (ActionPointToXY(new Position(x, y)) > actionPoints) finalScore[x, y] = -BIGNUM;
                        switch (AIHeroData.dict[aUnLockedWaitListID[i]].aiHeroMode) {
                            case (int)AIHeroData.AIMode.Balance:
                            case (int)AIHeroData.AIMode.MaxAttackDistance:
                                if (HeroData.ItemDict[aUnLockedWaitListID[i]].hp <= expectBeDamage[x, y])
                                    finalScore[x, y] = -BIGNUM; //一上場下一回合就被集火秒殺實在太蠢，要避免這種事發生。
                                else //殘餘血量(假設為10)百分比 乘上權重 約400~3500
                                    finalScore[x, y] += (int)(((float)HeroData.ItemDict[aUnLockedWaitListID[i]].hp - expectBeDamage[x, y]) / 10 * 100 * (int)WaitingWeight.BeDamage * 25);

                                //靠近隊友。 影響格數 2，分數 = 距離的倒數 * 100 * 權重 ~=  150~1000
                                finalScore[x, y] += nearTeammate[x, y] * (int)WaitingWeight.NearTeammate;

                                //靠近敵人。 影響格數 2，分數 = 距離的倒數 * 100 * 權重 ~= 300~2000
                                //finalScore[x, y] += nearEnemy[x, y] * (int)WaitingWeight.NearEnemy;

                                //靠近砲塔
                                finalScore[x, y] += nearTurret[x, y] * (int)WaitingWeight.NearTurret;

                                //如果在這個位置上場，能打到越多英雄分數越高
                                foreach (HeroHandler hero in bc.allHeroHandlerList) {
                                    if (XYCanHitB(aUnLockedWaitListID[i], new Position(x, y), hero, SkillDistanceType.Any))
                                        finalScore[x, y] += 5000;
                                }

                                if (finalScore[x, y] >= tmpScore) {
                                    if (Random.Range(0, 2) == 1) {
                                        tmpScore = finalScore[x, y]; tmpPosX = x; tmpPosY = y;
                                    }
                                }
                                break;
                            case (int)AIHeroData.AIMode.Escape:
                                if (HeroData.ItemDict[aUnLockedWaitListID[i]].hp <= expectBeDamage[x, y])
                                    finalScore[x, y] = -BIGNUM; //一上場下一回合就被集火秒殺實在太蠢，要避免這種事發生。
                                else //殘餘血量百分比 乘上權重 約400~3500
                                    finalScore[x, y] += (int)(((float)HeroData.ItemDict[aUnLockedWaitListID[i]].hp - expectBeDamage[x, y]) / 10 * 100 * (int)WaitingWeight.BeDamage * 4);

                                //靠近隊友。 影響格數 2，分數 = 距離的倒數 * 100 * 權重 ~=  150~1000
                                finalScore[x, y] += nearTeammate[x, y] * (int)WaitingWeight.NearTeammate * 3;

                                //靠近敵人。 影響格數 2，分數 = 距離的倒數 * 100 * 權重 ~= 300~2000
                                finalScore[x, y] += nearEnemy[x, y] * (int)WaitingWeight.NearEnemy / 2;

                                //靠近砲塔
                                finalScore[x, y] += nearTurret[x, y] * (int)WaitingWeight.NearTurret;

                                if (finalScore[x, y] > tmpScore) { tmpScore = finalScore[x, y]; tmpPosX = x; tmpPosY = y; }

                                break;
                            case (int)AIHeroData.AIMode.MaxDamage:
                                if (HeroData.ItemDict[aUnLockedWaitListID[i]].hp <= expectBeDamage[x, y])
                                    finalScore[x, y] = -BIGNUM; //一上場下一回合就被集火秒殺實在太蠢，要避免這種事發生。
                                else //殘餘血量百分比 乘上權重 約400~3500
                                    finalScore[x, y] += (int)(((float)HeroData.ItemDict[aUnLockedWaitListID[i]].hp - expectBeDamage[x, y]) / 10 * 100 * (int)WaitingWeight.BeDamage);

                                //靠近隊友。 影響格數 2，分數 = 距離的倒數 * 100 * 權重 ~=  150~1000
                                finalScore[x, y] += nearTeammate[x, y] * (int)WaitingWeight.NearTeammate * 3;

                                //靠近敵人。 影響格數 2，分數 = 距離的倒數 * 100 * 權重 ~= 300~2000
                                finalScore[x, y] += nearEnemy[x, y] * (int)WaitingWeight.NearEnemy * 2;

                                //靠近砲塔
                                finalScore[x, y] += nearTurret[x, y] * (int)WaitingWeight.NearTurret;

                                if (finalScore[x, y] > tmpScore) { tmpScore = finalScore[x, y]; tmpPosX = x; tmpPosY = y; }
                                break;
                            case (int)AIHeroData.AIMode.Tank:
                                if (HeroData.ItemDict[aUnLockedWaitListID[i]].hp <= expectBeDamage[x, y] * 2)
                                    finalScore[x, y] = -BIGNUM; //一上場下一回合就被集火秒殺實在太蠢，要避免這種事發生。
                                else //殘餘血量百分比 乘上權重 約400~3500
                                    finalScore[x, y] += (int)(((float)HeroData.ItemDict[aUnLockedWaitListID[i]].hp - expectBeDamage[x, y]) / 10 * 100 * (int)WaitingWeight.BeDamage);

                                //靠近隊友。 影響格數 2，分數 = 距離的倒數 * 100 * 權重 ~=  150~1000
                                finalScore[x, y] += nearTeammate[x, y] * (int)WaitingWeight.NearTeammate;

                                //靠近敵人。 影響格數 2，分數 = 距離的倒數 * 100 * 權重 ~= 300~2000
                                finalScore[x, y] += nearEnemy[x, y] * (int)WaitingWeight.NearEnemy * 2;

                                //靠近砲塔
                                finalScore[x, y] += nearTurret[x, y] * (int)WaitingWeight.NearTurret;

                                if (finalScore[x, y] > tmpScore) { tmpScore = finalScore[x, y]; tmpPosX = x; tmpPosY = y; }
                                break;
                        }
                    }
            bestScoreDict.Add(aUnLockedWaitListID[i], finalScore.Cast<int>().Max());
            bestPosXYDict.Add(aUnLockedWaitListID[i], new int[] { tmpPosX, tmpPosY });
        }

        //[TODO] 只有決定最好的位置，還沒為個別英雄決定不同上場分數，頂多是選擇上場不會馬上死的英雄而已
        //int randomNum = Random.Range(0, aWaitingListID.Count);
        bestWaitingHeroID = aUnLockedWaitListID[waitingListOrder[0]];
        bestWaitingHeroPosXY = bestPosXYDict[bestWaitingHeroID];
        bestWaitingIndex = waitingListOrder[0];//randomNum;
        //actionToBeDone.Add(new Action(ActionType.CallWaitingHero, new int[] { randomNum }, bestWaitingHeroPosXY));

        ec.waitinghero += Time.realtimeSinceStartup - timer;
    }

#endregion

#region AI假裝自己像個人在棋盤上進行動作(已經完成搜尋，決定好要怎麼走了)
    IEnumerator StartDoAction(List<Action> listActSet) {
        while (IsInputOn() == false) yield return new WaitForSeconds(0.1f);
        foreach (Action actionSet in listActSet) {
            if (actionSet.userPos == null || actionSet.userPos[0] == -1 || actionSet.type == ActionType.DoNothing) //means "Do Nothing"
                continue;
            if (bc.NowLockType != BattleController.LockType.None)
                bc.NowLockType = BattleController.LockType.None;
            if (bc.NowCursorPosition != CursorPosition.Chessboard)
                bc.NowCursorPosition = CursorPosition.Chessboard;


            switch (actionSet.type) {
                case ActionType.HeroMove:
                    yield return new WaitForSeconds(Random.Range(lowestWaitTine, mostWaitTime));
                    MoveCursorToTarget(actionSet.userPos[0], actionSet.userPos[1]);

                    yield return new WaitForSeconds(Random.Range(lowestWaitTine, mostWaitTime));
                    PressEnter();//模擬點選該英雄有時會失敗,原因待查

                    yield return new WaitForSeconds(Random.Range(lowestWaitTine, mostWaitTime));
                    MoveCursorToTarget(actionSet.targetPos[0], actionSet.targetPos[1]);

                    yield return new WaitForSeconds(Random.Range(lowestWaitTine, mostWaitTime));
                    PressEnter();

                    //TODO: 有時不明原因會執行失敗, 先暫時這樣:移動失敗就補做吧. 有空再找bug,
                    GameObject gameObject = bc.chessboardYX[actionSet.targetPos[1], actionSet.targetPos[0], (int)BattleController.ChessboardItem.Unit];
                    if (gameObject == null) {
                        bc.DoHeroMove(actionSet.userPos[0], actionSet.userPos[1], actionSet.targetPos[0], actionSet.targetPos[1]);
                    }

                    break;
                case ActionType.HeroSkill:
                    yield return new WaitForSeconds(Random.Range(lowestWaitTine, mostWaitTime));
                    MoveCursorToTarget(actionSet.userPos[0], actionSet.userPos[1]);

                    yield return new WaitForSeconds(Random.Range(lowestWaitTine, mostWaitTime));
                    PressEnter();

                    yield return new WaitForSeconds(Random.Range(lowestWaitTine, mostWaitTime));
                    yield return StartCoroutine(
                        bc.DoHeroSkill(bc.chessboardYX[actionSet.userPos[1], actionSet.userPos[0],
                        (int)BattleController.ChessboardItem.Unit].GetComponent<HeroHandler>(), actionSet.targetPos, false));

                    //yield return new WaitForSeconds(Random.Range(lowestWaitTine, mostWaitTime));
                    //MoveCursorToTarget(actionSet.targetPos[0], actionSet.targetPos[1]);

                    //yield return new WaitForSeconds(Random.Range(lowestWaitTine , mostWaitTime));
                    //PressEnter();
                    break;
                case ActionType.UseSummonerSkill:
                    yield return new WaitForSeconds(Random.Range(1.0f, 1.2f));
                    SetCursorPosition(NowTeamPlayerSkillCursorPos);
                    int nowIndex_pSkill = bc.nowGameTeam == GameTeam.A ? bc.p1PlayerSkillIndex : bc.p2PlayerSkillIndex;

                    for (int i = 0; i < psi.Length; i++) {
                        PlayerSkillHandler psh = psi[i].GetComponent<PlayerSkillHandler>();
                        if (psh != null && psh.playerSkillID == actionSet.userPos[0]) {
                            bc.UsePlayerSkill(psh, bc.nowGameTeam);
                            break;
                        }
                    }
                    //if (psh == null) MyDebug.LogError("PlayerSkillHandler is null!!");
                    int psid = actionSet.userPos[0];
                    switch (psid) {
                        case (int)PlayerSkillID.Ignite:
                        case (int)PlayerSkillID.Heal:
                        case (int)PlayerSkillID.Gold:
                            yield return new WaitForSeconds(Random.Range(lowestWaitTine, mostWaitTime));
                            bc.nowCursorX = actionSet.targetPos[0]; bc.nowCursorY = actionSet.targetPos[1];
                            yield return StartCoroutine(bc.DoPlayerSkill(false));
                            yield return new WaitForSeconds(Random.Range(1.5f, 2.2f));
                            break;
                        case (int)PlayerSkillID.Ghost:
                            yield return new WaitForSeconds(Random.Range(lowestWaitTine, mostWaitTime));
                            bc.nowCursorX = actionSet.userPos[1]; bc.nowCursorY = actionSet.userPos[2];
                            yield return StartCoroutine(bc.DoPlayerSkill(false));

                            //使用鬼步會有動畫特效，確認等夠久再開始移動英雄
                            yield return new WaitForSeconds(Random.Range(2.5f, 3.0f));
                            bc.NowLockType = BattleController.LockType.None;
                            SetCursorPosition(CursorPosition.Chessboard);

                            MoveCursorToTarget(actionSet.userPos[1], actionSet.userPos[2]);
                            yield return new WaitForSeconds(Random.Range(lowestWaitTine, mostWaitTime));
                            PressEnter();

                            yield return new WaitForSeconds(Random.Range(lowestWaitTine, mostWaitTime));
                            MoveCursorToTarget(actionSet.targetPos[0], actionSet.targetPos[1]);

                            yield return new WaitForSeconds(Random.Range(lowestWaitTine, mostWaitTime));
                            PressEnter();
                            break;
                        case (int)PlayerSkillID.Flash:
                            yield return new WaitForSeconds(Random.Range(lowestWaitTine, mostWaitTime));
                            bc.nowCursorX = actionSet.userPos[1]; bc.nowCursorY = actionSet.userPos[2];
                            yield return StartCoroutine(bc.DoPlayerSkill(false));

                            yield return new WaitForSeconds(Random.Range(lowestWaitTine, mostWaitTime));
                            bc.nowCursorX = actionSet.targetPos[0]; bc.nowCursorY = actionSet.targetPos[1];
                            yield return StartCoroutine(bc.DoPlayerSkill_DoubleLock(false));
                            break;
                        case (int)PlayerSkillID.Teleport:
                            yield return new WaitForSeconds(Random.Range(lowestWaitTine, mostWaitTime));
                            SetCursorPosition(NowTeamWaitingCursorPos);

                            for (int i = 0; i < aWaitingListID.Count; i++) {
                                int aIndex = i % aWaitingListID.Count;
                                if (aWaitingListID.Count < bc.canSeeWaitingHeroCount) aIndex = i % bc.canSeeWaitingHeroCount;
                                int aHeroDesignerID = aWaitingListID[aIndex];
                                if (aHeroDesignerID == actionSet.userPos[1]) {
                                    bc.lockID = i;
                                    if(bc.nowGameTeam == GameTeam.A) bc.p1WaitingImageListIndex = i;
                                    else bc.p2WaitingImageListIndex = i;
                                    break;
                                }
                            }

                            yield return new WaitForSeconds(Random.Range(lowestWaitTine, mostWaitTime));
                            PressEnter();

                            yield return new WaitForSeconds(Random.Range(1.0f, 1.5f));
                            SetCursorPosition(CursorPosition.Chessboard);
                            yield return new WaitForSeconds(Random.Range(0.2f, 0.3f));
                            MoveCursorToTarget(teleportPos[0], teleportPos[1]);
                            yield return new WaitForSeconds(Random.Range(0.2f, 0.3f));
                            PressEnter();

                            //衝鋒上場會有動畫 確認有等夠久在進行動作
                            yield return new WaitForSeconds(Random.Range(3.0f, 3.5f));
                            //SetCursorPosition(CursorPosition.Chessboard);
                            MoveCursorToTarget(teleportPos[0], teleportPos[1]);
                            yield return new WaitForSeconds(Random.Range(0.2f, 0.3f));
                            PressEnter();

                            yield return new WaitForSeconds(Random.Range(lowestWaitTine, mostWaitTime));
                            yield return StartCoroutine(
                            bc.DoHeroSkill(bc.chessboardYX[teleportPos[1], teleportPos[0],
                            (int)BattleController.ChessboardItem.Unit].GetComponent<HeroHandler>(), actionSet.targetPos, false));
                            break;
                    }


                    //yield return new WaitForSeconds(Random.Range(lowestWaitTine, mostWaitTime));
                    //bc.NowCursorPosition = CursorPosition.Chessboard;
                    //MoveCursorToTarget(actionSet.targetPos[0], actionSet.targetPos[1]);

                    //yield return new WaitForSeconds(Random.Range(lowestWaitTine, mostWaitTime));
                    //PressEnter();

                    break;
                case ActionType.CallWaitingHero:

                    WaitingHeroHandler aWaitingHeroHandler = bc.p2WaitingObjArray[3].GetComponent<WaitingHeroHandler>();
                    if (actionPoints > 1) {
                        for (int i = 0; i < 30; i++) {
                            if (aWaitingHeroHandler.heroID == actionSet.userPos[0]) {
                                break;
                            }
                            bc.isP2WaitingPlaceMoving = false;
                            yield return StartCoroutine(bc.DoMoveWaitingList(bc.p2WaitingParentObj, bc.p2WaitingObjArray, bc.p2WaitingImageList, GameTeam.B, BattleController.KeyBoardAspect.Down));
                            //bc.p2WaitingImageListIndex++;
                            aWaitingHeroHandler = bc.p2WaitingObjArray[3].GetComponent<WaitingHeroHandler>();

                            yield return new WaitForSeconds(0.1f);
                        }
                        int[] aRndPlace = new int[] { actionSet.targetPos[0], actionSet.targetPos[1] };
                        yield return StartCoroutine(bc.CallHeroFromWaitingPlace(bc.nowGameTeam, bc.nowGameTeam, aRndPlace[0], aRndPlace[1], true, true, false));
                    }

                    ///////////////////

                    /*yield return new WaitForSeconds(Random.Range(lowestWaitTine, mostWaitTime));
                    bc.NowCursorPosition = bc.nowGameTeam == GameTeam.A ? CursorPosition.P1HeroWaiting : CursorPosition.P2HeroWaiting;
                    int nowIndex = bc.nowGameTeam == GameTeam.A ? bc.p1WaitingImageListIndex : bc.p2WaitingImageListIndex;

                    bool inverse = actionSet.userPos[0] - nowIndex > 0 ? false : true;
                    for (int i = 0; i < Mathf.Abs(actionSet.userPos[0] - nowIndex); i++) {
                        yield return new WaitForSeconds(Random.Range(0.15f, 0.35f));
                        if (inverse)
                            bc.DoKeyboard(BattleController.KeyBoardAspect.Up);
                        else
                            bc.DoKeyboard(BattleController.KeyBoardAspect.Down);
                    }


                    yield return new WaitForSeconds(Random.Range(lowestWaitTine, mostWaitTime));
                    //Debug.Log("1." + bc.NowCursorPosition);
                    PressEnter();
                    //Debug.Log("2." + bc.NowCursorPosition);

                    yield return new WaitForSeconds(Random.Range(lowestWaitTine, mostWaitTime));
                    bc.NowCursorPosition = CursorPosition.Chessboard;
                    //Debug.Log("targetPos(" + actionSet.targetPos[0] +"," + actionSet.targetPos[1] + ")");
                    MoveCursorToTarget(actionSet.targetPos[0], actionSet.targetPos[1]);

                    yield return new WaitForSeconds(Random.Range(lowestWaitTine, mostWaitTime));
                    //Debug.Log("3." + bc.NowCursorPosition);
                    PressEnter();
                    //Debug.Log("4." + bc.NowCursorPosition);*/
                    break;
                case ActionType.DoNothing:
                    break;
            }
        }

        yield return new WaitForSeconds(1.5f);
        bc.ChangeRoundButtonListener();
    }

    void MoveCursorToTarget(int targetX, int targetY) {
        bc.DoMoveCursor(targetX, targetY);
    }

    void PressEnter() {
        bc.KeyboardEnterHandler();
    }

    void SetCursorPosition(CursorPosition cursorPos) {
        bc.NowCursorPosition = cursorPos;
    }

    CursorPosition NowTeamWaitingCursorPos {
        get {
            if (bc.nowGameTeam == GameTeam.A)
                return CursorPosition.P1HeroWaiting;
            else
                return CursorPosition.P2HeroWaiting;
        }
    }

    CursorPosition NowTeamPlayerSkillCursorPos {
        get {
            if (bc.nowGameTeam == GameTeam.A)
                return CursorPosition.P1PlayerSkill;
            else
                return CursorPosition.P2PlayerSkill;
        }
    }
    #endregion


    #region Tools (Fakemove、轉換資料結構、判斷英雄狀況...等)
    IEnumerator AIFakeMove(Action[] acts) {
        //for Efficiency Counter
        float timer = Time.realtimeSinceStartup;

        foreach (Action action in acts) {
            switch (action.type) {
                case ActionType.DoNothing:
                    break;
                case ActionType.HeroMove:
                    bc.DoHeroMove(action.userPos[0], action.userPos[1], action.targetPos[0], action.targetPos[1]);
                    //bc.DoHeroMove_NoLock(action.userPos[0], action.userPos[1], action.targetPos[0], action.targetPos[1]);
                    break;
                case ActionType.HeroSkill:
                    yield return StartCoroutine(bc.DoHeroSkill(bc.chessboardYX[action.userPos[1], action.userPos[0], (int)BattleController.ChessboardItem.Unit].GetComponent<HeroHandler>(),
                        action.targetPos, true));
                    break;
                case ActionType.UseSummonerSkill:
                    int targetIndex = action.userPos[0] - 101;
                    PlayerSkillHandler psh = null;
                    bool isFounded = false; //是否有成功找到需要使用的召喚師技能
                    for (int i = 0; i < psi.Length; i++) {
                        psh = psi[i].GetComponent<PlayerSkillHandler>();
                        if (psh.playerSkillID == action.userPos[0]) {
                            isFounded = true;
                            break;
                        }
                    }

                    if (isFounded) {
                        switch ((PlayerSkillID)psh.playerSkillID) {
                            case PlayerSkillID.Ignite:
                            case PlayerSkillID.Heal:
                            case PlayerSkillID.Gold:
                                bc.UsePlayerSkill(psh, bc.nowGameTeam);
                                bc.nowCursorX = action.targetPos[0]; bc.nowCursorY = action.targetPos[1];
                                yield return StartCoroutine(bc.DoPlayerSkill(true));
                                break;
                            case PlayerSkillID.Ghost:
                                bc.UsePlayerSkill(psh, bc.nowGameTeam);
                                bc.nowCursorX = action.userPos[1]; bc.nowCursorY = action.userPos[2];
                                yield return StartCoroutine(bc.DoPlayerSkill(true));

                                yield return StartCoroutine(bc.DoHeroMove(action.userPos[1], action.userPos[2], action.targetPos[0], action.targetPos[1], true));
                                break;
                            case PlayerSkillID.Flash:
                                bc.UsePlayerSkill(psh, bc.nowGameTeam);
                                bc.nowCursorX = action.userPos[1]; bc.nowCursorY = action.userPos[2];
                                yield return StartCoroutine(bc.DoPlayerSkill(true));

                                bc.nowCursorX = action.targetPos[0]; bc.nowCursorY = action.targetPos[1];
                                yield return StartCoroutine(bc.DoPlayerSkill_DoubleLock(true));
                                break;
                            case PlayerSkillID.Teleport:
                                break;
                        }
                    }
                    break;
                case ActionType.CallWaitingHero:
                    //[WARNING] 直接召喚上場可能有BUG
                    //yield return StartCoroutine(bc.AddWaitingHeroToChessboardID(bc.nowGameTeam, bc.nowGameTeam, action.targetPos[0], action.targetPos[1], true, true, true));
                    
                    WaitingHeroHandler aWaitingHeroHandler = bc.p2WaitingObjArray[3].GetComponent<WaitingHeroHandler>();
                    if (actionPoints >= ActionPointToXY(new Position(action.targetPos[0], action.targetPos[1]))) {
                        for (int i = 0; i < 15; i++) {
                            if (aWaitingHeroHandler.heroID == action.userPos[0]) {
                                break;
                            }
                            //bc.isP2WaitingPlaceMoving = false;
                            //yield return StartCoroutine(bc.DoMoveWaitingList(bc.p2WaitingParentObj, bc.p2WaitingObjArray, bc.p2WaitingImageList, GameTeam.B, BattleController.KeyBoardAspect.Down));
                            bc.p2WaitingImageListIndex++;
                            aWaitingHeroHandler = bc.p2WaitingObjArray[3].GetComponent<WaitingHeroHandler>();

                            //yield return new WaitForSeconds(0.1f);
                        }
                        int[] aRndPlace = new int[] { action.targetPos[0], action.targetPos[1] };
                        //yield return StartCoroutine(bc.CallHeroFromWaitingPlace(bc.nowGameTeam, bc.nowGameTeam, aRndPlace[0], aRndPlace[1], true, true, true));
                        //純AI搜尋時, 建議不使用Coroutine, 才能省時間
                        bc.CallHeroFromWaitingPlace_ForAI(bc.nowGameTeam, bc.nowGameTeam, aRndPlace[0], aRndPlace[1], true, true);

                    } else
                        Debug.Log("call waiting hero error (actionPoints)");
                    
                    break;
            }
        }

        ec.fakemove += Time.realtimeSinceStartup - timer;
    }

    //快速版本的FakeMove without coroutine, 建議使用這個
    private void AIFakeMove_Fast(Action action) {
        //for Efficiency Counter
        float timer = Time.realtimeSinceStartup;

        switch (action.type) {
            case ActionType.DoNothing:
                break;
            case ActionType.HeroMove:
                bc.DoHeroMove(action.userPos[0], action.userPos[1], action.targetPos[0], action.targetPos[1]);
                break;
            case ActionType.UseSummonerSkill:
                int targetIndex = action.userPos[0] - 101;
                PlayerSkillHandler psh = null;
                bool isFounded = false; //是否有成功找到需要使用的召喚師技能
                for (int i = 0; i < psi.Length; i++) {
                    psh = psi[i].GetComponent<PlayerSkillHandler>();
                    if (psh.playerSkillID == action.userPos[0]) {
                        isFounded = true;
                        break;
                    }
                }

                if (isFounded) {
                    switch ((PlayerSkillID)psh.playerSkillID) {
                        case PlayerSkillID.Ignite:
                        case PlayerSkillID.Heal:
                        case PlayerSkillID.Gold:
                            bc.UsePlayerSkill(psh, bc.nowGameTeam);
                            bc.nowCursorX = action.targetPos[0];
                            bc.nowCursorY = action.targetPos[1];
                            bc.DoPlayerSkill_ForAI();
                            break;
                        case PlayerSkillID.Ghost:
                            bc.UsePlayerSkill(psh, bc.nowGameTeam);
                            bc.nowCursorX = action.userPos[1];
                            bc.nowCursorY = action.userPos[2];
                            bc.DoPlayerSkill_ForAI();

                            bc.DoHeroMove(action.userPos[1], action.userPos[2], action.targetPos[0], action.targetPos[1]);
                            break;
                        case PlayerSkillID.Flash:
                            bc.UsePlayerSkill(psh, bc.nowGameTeam);
                            bc.nowCursorX = action.userPos[1];
                            bc.nowCursorY = action.userPos[2];
                            bc.DoPlayerSkill_ForAI();

                            bc.nowCursorX = action.targetPos[0];
                            bc.nowCursorY = action.targetPos[1];
                            bc.DoPlayerSkill_DoubleLock_ForAI();
                            break;
                        case PlayerSkillID.Teleport:
                            break;
                    }
                }
                break;
            case ActionType.CallWaitingHero:
                //[WARNING] 直接召喚上場可能有BUG
                //yield return StartCoroutine(bc.AddWaitingHeroToChessboardID(bc.nowGameTeam, bc.nowGameTeam, action.targetPos[0], action.targetPos[1], true, true, true));

                WaitingHeroHandler aWaitingHeroHandler = bc.p2WaitingObjArray[3].GetComponent<WaitingHeroHandler>();
                if (actionPoints >= ActionPointToXY(new Position(action.targetPos[0], action.targetPos[1]))) {
                    for (int i = 0; i < 15; i++) {
                        if (aWaitingHeroHandler.heroID == action.userPos[0]) {
                            break;
                        }
                        //bc.isP2WaitingPlaceMoving = false;
                        //yield return StartCoroutine(bc.DoMoveWaitingList(bc.p2WaitingParentObj, bc.p2WaitingObjArray, bc.p2WaitingImageList, GameTeam.B, BattleController.KeyBoardAspect.Down));
                        bc.p2WaitingImageListIndex++;
                        aWaitingHeroHandler = bc.p2WaitingObjArray[3].GetComponent<WaitingHeroHandler>();

                    }
                    int[] aRndPlace = new int[] { action.targetPos[0], action.targetPos[1] };
                    bc.CallHeroFromWaitingPlace_ForAI(bc.nowGameTeam, bc.nowGameTeam, aRndPlace[0], aRndPlace[1], true, true);

                } else
                    Debug.Log("call waiting hero error (actionPoints)");
                break;
        }

        ec.fakemove += Time.realtimeSinceStartup - timer;
    }

    bool HeroCanMove(HeroHandler hh) {
        if (hh.NowMovePoint > 0 && actionPoints > 0)
            return true;
        else return false;
    }

    bool HeroCanSkill(HeroHandler hh) {
        if (hh.NowSkillPoint > 0 && actionPoints > 0)
            return true;
        else return false;
    }


    //判斷一隻英雄是不是在另一隻英雄的技能範圍內
    //A為要施放技能的英雄 B(Target) 為要被施放的角色(或座標)
    public enum SkillDistanceType {
        Friend,
        Enemy,
        Neutral,
        EnemyAndNetral,
        Any
    }

    //判斷棋盤上兩個位置的距離
    int GetChessboardDistance(Position center, Position target) {
        //判斷是否在射程內
        int[] aCenterPosition = center.ToArray();
        int[] aTargetPosition = target.ToArray();

        int aChessboardDistance = 0;
        for (int circle = 1; circle <= 10 && aChessboardDistance == 0; ++circle) {
            for (int adjust = 0; adjust < circle; ++adjust) {
                if ((aTargetPosition[0] == aCenterPosition[0] - adjust && aTargetPosition[1] == aCenterPosition[1] - circle) // up
                    || (aTargetPosition[0] == aCenterPosition[0] + circle - adjust && aTargetPosition[1] == aCenterPosition[1] - adjust) // right & up
                    || (aTargetPosition[0] == aCenterPosition[0] + circle && aTargetPosition[1] == aCenterPosition[1] + circle - adjust) // right & down
                    || (aTargetPosition[0] == aCenterPosition[0] + adjust && aTargetPosition[1] == aCenterPosition[1] + circle) // down
                    || (aTargetPosition[0] == aCenterPosition[0] - circle + adjust && aTargetPosition[1] == aCenterPosition[1] + adjust) // left & down
                    || (aTargetPosition[0] == aCenterPosition[0] - circle && aTargetPosition[1] == aCenterPosition[1] - circle + adjust)) { // left & up
                    aChessboardDistance = circle;
                    break;
                }
            }
        }
        return aChessboardDistance;
    }

    bool IsInDistance(Position skiller, Position target, int aChessboardDistance, SkillDataItem aSkillDataItem, SkillDistanceType type) {
        //一個技能可能因為目標對象不同而有不同的範圍(例如治療比傷害打更遠)，如果有多重範圍，通常取最遠那個就好
        int distance_bin = 1;
        switch (type) {
            case SkillDistanceType.Friend:
                distance_bin = aSkillDataItem.friendDistance;
                break;
            case SkillDistanceType.Enemy:
                distance_bin = aSkillDataItem.enemyDistance;
                break;
            case SkillDistanceType.Neutral:
                distance_bin = aSkillDataItem.neutralDistance;
                break;
            case SkillDistanceType.EnemyAndNetral:
                distance_bin = Mathf.Max(aSkillDataItem.enemyDistance, aSkillDataItem.neutralDistance);
                break;
            case SkillDistanceType.Any:
                distance_bin = Mathf.Max(aSkillDataItem.friendDistance, aSkillDataItem.enemyDistance, aSkillDataItem.neutralDistance);
                break;
        }


        char[] aDistanceArray = IntToArray.IntToBinaryReverseCharArray(distance_bin);
        if (aDistanceArray.Length > aChessboardDistance && aDistanceArray[aChessboardDistance].Equals('1')) {
            if (aChessboardDistance > 1
                && aSkillDataItem.isDesignate == 0
                && bc.IsTheSkillBeTerrainBlock(skiller.ToArray(), target.ToArray())) { // 受地形影響又被擋住
                return false;
            } else { // 打的到
                return true;
            }
        }
        return false;
    }

    bool ACanHitB(HeroHandler aHeroHandler, HeroHandler aTargetHeroHandler, SkillDistanceType type) {
        return ACanHitXY(aHeroHandler, aTargetHeroHandler.PositionXY, type);
    }

    bool ACanHitXY(HeroHandler aHeroHandler, int[] aPositionXY, SkillDistanceType type) {
        //判斷是否在射程內
        Position AHeroPos = new Position(aHeroHandler.PositionXY);
        Position BHeroPos = new Position(aPositionXY);
        int aChessboardDistance = GetChessboardDistance(AHeroPos, BHeroPos);
        SkillDataItem aSkillDataItem = aHeroHandler.skillDataItem;

        return IsInDistance(AHeroPos, BHeroPos, aChessboardDistance, aSkillDataItem, type);
    }

    //專門用來判斷沒有HeroHandler的英雄能否打到B英雄(ex:等待區的英雄能否打到某隻已經在場上的英雄)
    bool XYCanHitB(int heroID, Position pos, HeroHandler bhh, SkillDistanceType type) {
        Position BHeroPos = new Position(bhh.PositionXY);
        int aChessboardDistance = GetChessboardDistance(pos, BHeroPos);
        SkillDataItem aSkillDataItem = bc.gameData.skills.getSkillData(HeroData.ItemDict[heroID].skillID);

        return IsInDistance(pos, BHeroPos, aChessboardDistance, aSkillDataItem, type);
    }


    bool ACanHitBInOneMove(HeroHandler aHeroHandler, HeroHandler aTargetHeroHandler, SkillDistanceType type) {
        return ACanHitXYInOneMove(aHeroHandler, aTargetHeroHandler.PositionXY, type);
    }

    //搭配移動一格的話，A 是否能打到在XY格子上的英雄? (包括自身不移動)
    //比單純的ACanHitXY還要多耗數倍的計算量，小心使用
    bool ACanHitXYInOneMove(HeroHandler aHeroHandler, int[] aPositionXY, SkillDistanceType type) {
        bool canHit = false;

        //backup before move
        //我們有可能會把已經有英雄的格子再移動一隻英雄上去，這會造成錯誤，所以我們先直接備份一個本來的棋盤
        GameObject[,,] chessBoardBackup = new GameObject[chessboardY, chessboardX, bc.chessboardLength];
        System.Array.Copy(bc.chessboardYX, chessBoardBackup, bc.chessboardYX.Length);
        int movepointBackup = aHeroHandler.nowMovePoint;
        int x_beforeMove = aHeroHandler.PositionXY[0], y_beforeMove = aHeroHandler.PositionXY[1];
        //int backupIndex = bc.RecordData();

        //用IsXYCanAddHero有考慮到路被隊友擋住，但沒有考慮到隊友可以移開
        //用IsXYBoardType沒有考慮到格子尚有其他隊友，但反過來說有考慮到隊友移開的可能性
        if (ACanHitXY(aHeroHandler, aPositionXY, type)) //No Move，在原地打打看，因為目標可能距離我們僅一格， 我們移動到他臉上這類的
            canHit = true;

        if (bc.IsXYBoardType(aHeroHandler.PositionXY[0], aHeroHandler.PositionXY[1] - 1, BoardHandler.BoardType.Normal)) //up
        {
            bc.DoHeroMove(x_beforeMove, y_beforeMove, x_beforeMove, y_beforeMove - 1);
            if (ACanHitXY(aHeroHandler, aPositionXY, type))
                canHit = true;
            bc.UndoHeroMove(aHeroHandler.PositionXY[0], aHeroHandler.PositionXY[1], x_beforeMove, y_beforeMove);
        }
        if (bc.IsXYBoardType(aHeroHandler.PositionXY[0] + 1, aHeroHandler.PositionXY[1], BoardHandler.BoardType.Normal)) //right-up
        {
            bc.DoHeroMove(x_beforeMove, y_beforeMove, x_beforeMove + 1, y_beforeMove);
            if (ACanHitXY(aHeroHandler, aPositionXY, type))
                canHit = true;
            bc.UndoHeroMove(aHeroHandler.PositionXY[0], aHeroHandler.PositionXY[1], x_beforeMove, y_beforeMove);
        }
        if (bc.IsXYBoardType(aHeroHandler.PositionXY[0] + 1, aHeroHandler.PositionXY[1] + 1, BoardHandler.BoardType.Normal)) //right-down
        {
            bc.DoHeroMove(x_beforeMove, y_beforeMove, x_beforeMove + 1, y_beforeMove + 1);
            if (ACanHitXY(aHeroHandler, aPositionXY, type))
                canHit = true;
            bc.UndoHeroMove(aHeroHandler.PositionXY[0], aHeroHandler.PositionXY[1], x_beforeMove, y_beforeMove);
        }
        if (bc.IsXYBoardType(aHeroHandler.PositionXY[0], aHeroHandler.PositionXY[1] + 1, BoardHandler.BoardType.Normal)) //down
        {
            bc.DoHeroMove(x_beforeMove, y_beforeMove, x_beforeMove, y_beforeMove + 1);
            if (ACanHitXY(aHeroHandler, aPositionXY, type))
                canHit = true;
            bc.UndoHeroMove(aHeroHandler.PositionXY[0], aHeroHandler.PositionXY[1], x_beforeMove, y_beforeMove);
        }
        if (bc.IsXYBoardType(aHeroHandler.PositionXY[0] - 1, aHeroHandler.PositionXY[1], BoardHandler.BoardType.Normal)) //left-down
        {
            bc.DoHeroMove(x_beforeMove, y_beforeMove, x_beforeMove - 1, y_beforeMove);
            if (ACanHitXY(aHeroHandler, aPositionXY, type))
                canHit = true;
            bc.UndoHeroMove(aHeroHandler.PositionXY[0], aHeroHandler.PositionXY[1], x_beforeMove, y_beforeMove);
        }
        if (bc.IsXYBoardType(aHeroHandler.PositionXY[0] - 1, aHeroHandler.PositionXY[1] - 1, BoardHandler.BoardType.Normal)) //left-up
        {
            bc.DoHeroMove(x_beforeMove, y_beforeMove, x_beforeMove - 1, y_beforeMove - 1);
            if (ACanHitXY(aHeroHandler, aPositionXY, type))
                canHit = true;
            bc.UndoHeroMove(aHeroHandler.PositionXY[0], aHeroHandler.PositionXY[1], x_beforeMove, y_beforeMove);
        }

        //restore some data
        System.Array.Copy(chessBoardBackup, bc.chessboardYX, bc.chessboardYX.Length);
        aHeroHandler.nowMovePoint = movepointBackup;
        //bc.LoadRecordData(backupIndex, true);

        return canHit;
    }


    //判斷這個英雄是不是有無法扣血的buff
    //如果這個英雄是可以攻擊的，回傳true
    bool IsHeroAttackable(HeroHandler hh) {
        if (hh.isBuffFlag[(int)BuffData.FlagIndex.CantDamage] == false)
            return true;
        else
            return false;
    }

    bool IsHeroHealable(HeroHandler hh) {
        if (hh.isBuffFlag[(int)BuffData.FlagIndex.CantHeal] == false)
            return true;
        else
            return false;
    }

    bool IsHeroMoveable(HeroHandler hh) {
        if (hh.isBuffFlag[(int)BuffData.FlagIndex.CantMove] == false)
            return true;
        else
            return false;
    }

    bool IsHeroFlashable(HeroHandler hh) {
        return true;
    }

    //判斷這個英雄是否有位移敵人的技能 ex:牛、烏爾加特
    bool ACanShiftEnemy(HeroHandler hh) {
        char[] goalType = IntToArray.IntToBinaryReverseCharArray(hh.skillDataItem.targetDisplacementGoalType);
        if (hh.skillDataItem.targetDisplacementEffect != 0 //擁有位移目標的技能
            && goalType[1].Equals('1')) //目標對象有包括敵人這個選項
        {
            return true;
        }
        return false;
    }

    //判斷這個英雄是否有位移隊友的技能 ex:瑟雷西
    bool ACanShiftTeammate(HeroHandler hh) {
        char[] goalType = IntToArray.IntToBinaryReverseCharArray(hh.skillDataItem.targetDisplacementGoalType);
        if (hh.skillDataItem.targetDisplacementEffect != 0 //擁有位移目標的技能
            && goalType[0].Equals('1')) //目標對象有包括友方這個選項
        {
            return true;
        }
        return false;
    }


    void FinishSearch() {
        bc.NowLockType = BattleController.LockType.None;
        bc.isAI = false; //把ai關掉 StartDoAction才會有畫面效果

        //Clear Data
        before_playerSkillToBeDone = null;
        after_playerSkillToBeDone = null;
        bestWaitingHeroID = -1;
        bestWaitingHeroPosXY = null;
        bestWaitingIndex = -1;
        //bc.ClearAllRecordData();
    }

    //根據現在是哪一隊，決定AI是哪一隊，一次完整的StartAI只會call一次，在之後的搜尋中不會更動AI隊伍
    void DecideWhichTeam() {
        //不能在foreach中改動list內容, 我們要new一個出來才行(don't modify contents when iterating)
        switch (bc.nowGameTeam) {
            case GameTeam.A:
                //aHeroHandlerList = new List<HeroHandler>(bc.p1HeroHandlerList);
                aHeroHandlerList = bc.p1HeroHandlerList;
                aBuildingHandlerList = bc.p1BuildingHandlerList;
                aUnLockedWaitListID = GenUnLockedHeroList(bc.p1HeroLockArray, bc.p1HeroIDIntList);//bc.p1HeroIDIntList;
                aWaitingListID = bc.p1HeroIDIntList;
                psi = bc.p1PlayerSkillImageArray;

                //bHeroHandlerList = new List<HeroHandler>(bc.p2HeroHandlerList);
                bHeroHandlerList = bc.p2HeroHandlerList;
                bBuildingHandlerList = bc.p2BuildingHandlerList;
                bUnLockedWaitListID = GenUnLockedHeroList(bc.p2HeroLockArray, bc.p2HeroIDIntList);//bc.p2HeroIDIntList;
                bWaitingListID = bc.p2HeroIDIntList;

                break;
            case GameTeam.B:
                //aHeroHandlerList = new List<HeroHandler>(bc.p2HeroHandlerList);
                aHeroHandlerList = bc.p2HeroHandlerList;
                aBuildingHandlerList = bc.p2BuildingHandlerList;
                aUnLockedWaitListID = GenUnLockedHeroList(bc.p2HeroLockArray, bc.p2HeroIDIntList);//bc.p2HeroIDIntList;
                aWaitingListID = bc.p2HeroIDIntList;
                psi = bc.p2PlayerSkillImageArray;

                //bHeroHandlerList = new List<HeroHandler>(bc.p1HeroHandlerList);
                bHeroHandlerList = bc.p1HeroHandlerList;
                bBuildingHandlerList = bc.p1BuildingHandlerList;
                bUnLockedWaitListID = GenUnLockedHeroList(bc.p1HeroLockArray, bc.p1HeroIDIntList);//bc.p1HeroIDIntList;
                bWaitingListID = bc.p1HeroIDIntList;
                break;
            case GameTeam.C:
                MyDebug.Log("WTF is this?!?!");
                break;
        }
    }

    void DecideWhichHeroFirst() {
        //有最多移動可能的英雄優先動他
        List<HeroHandler> origin = aHeroHandlerList;
        List<HeroHandler> newList = new List<HeroHandler>();
        List<int> availableMovesCount = new List<int>();

        for (int i = 0; i < origin.Count; i++) {
            availableMovesCount.Add(0);
            bc.UpdateBoardHeroActionArrayXY(ActionType.HeroMove, origin[i].PositionXY[0], origin[i].PositionXY[1]);
            for (int y = 0; y < chessboardY; y++)
                for (int x = 0; x < chessboardX; x++) {
                    if (bc.boardHeroActionArrayXY[x, y, (int)BoardHeroActionArrayItem.BoardHeroAction] == (int)BoardHeroAction.CanMove)
                        availableMovesCount[i]++;
                }
        }

        while (origin.Count > 0) {
            int maxValue = availableMovesCount.Max();
            int maxIndex = availableMovesCount.IndexOf(maxValue);
            newList.Add(origin[maxIndex]);

            availableMovesCount.RemoveAt(maxIndex);
            origin.RemoveAt(maxIndex);
        }
        //finally, we assign the new order list to aHeroHandlerList
        aHeroHandlerList.Clear();
        foreach (HeroHandler hero in newList) {
            aHeroHandlerList.Add(hero);
        }
    }

    bool IsInputOn() {
        //if (bc.isTouchEnable == true &&
        //bc.isKeyboardOK == true &&
        //bc.changeTurnButton.interactable == true)
        //    return true;
        if (bc.NowLockType == BattleController.LockType.None
            && !bc.isPassiveSkillShowing)
            return true;
        return false;
    }

    //回傳一上場就會受到的傷害總量
    //ex: 凱特琳、伊芙琳......等一上場就會遭到攻擊的全場被動
    int GlobalDamage_CallToChessboard() {
        int damageSum = 0;

        foreach (HeroHandler bhh in bHeroHandlerList) {
            if (bhh.heroId == 115) //凱特琳
            {
                damageSum += bhh.passiveSkillDataItemArray[0].value1;
            }
        }

        return damageSum;
    }

    /***如果在該格子降落，最多會受到多少傷害? 回傳每個格子會受到多少傷害的二維陣列***/
    int[,] expectBeDamaged_CallToChessboard() {
        int[,] expectBeDamaged = new int[chessboardX, chessboardY];
        int globalDamage = GlobalDamage_CallToChessboard(); //一上場必定會受到的傷害
        for (int y = 0; y < chessboardY; y++)
            for (int x = 0; x < chessboardX; x++) {
                expectBeDamaged[x, y] += globalDamage;
                foreach (HeroHandler bhh in bHeroHandlerList) {
                    if (ACanHitXYInOneMove(bhh, new int[] { x, y }, SkillDistanceType.Enemy))
                        expectBeDamaged[x, y] += bhh.skillDataItem.targetDamage;
                }
                foreach (BuildingHandler bh in bBuildingHandlerList) {
                    if (IsInNexusAttackRange(new Position(bh.PositionXY), new Position (x, y)) && bNexusAttackTarget == null)
                        expectBeDamaged[x, y] += 2;
                }
            }
        return expectBeDamaged;
    }

    //使用英雄ID搜尋資料表中這隻英雄對目標的攻擊力
    //通常用在英雄沒有實體存在場上的時候(板凳區英雄)，抓取不到HeroHandler
    int GetTargetDamageByHeroId(int heroId) {
        //HeroDataItem heroDataItem = bc.gameData.heroes.getHeroData(heroId);
        HeroDataItem heroDataItem = HeroData.ItemDict[heroId];
        SkillDataItem skillDataItem = bc.gameData.skills.getSkillData(heroDataItem.skillID);
        return skillDataItem.targetDamage;
    }

    bool IsDominatingOnNowChessboard() {
        int aAttack = 0, bAttack = 0;
        int aHP = 0, bHP = 0;

        foreach(HeroHandler ahh in aHeroHandlerList) {
            if(ahh.heroId != 2001) { //先不把小兵考慮進來
                aAttack += ahh.attack;
                aHP += ahh.nowHP;
            }
        }
        foreach(HeroHandler bhh in bHeroHandlerList) {
            if (bhh.heroId != 2001) {
                bAttack += bhh.attack;
                bHP += bhh.nowHP;
            }
        }

        int aSum = aAttack + aHP;
        int bSum = bAttack + bHP;

        if (aSum >= bSum)
            return true;
        else
            return false;

    }

    //尋找點燃的PlayerSkillHandler
    PlayerSkillHandler FindPlayerSkill(PlayerSkillID id) {
        for (int i = 0; i < psi.Length; i++) {
            PlayerSkillHandler psh = psi[i].GetComponent<PlayerSkillHandler>();
            if (psh != null && psh.playerSkillID == (int)id)
                return psh;
        }
        return null;
    }

    //算出現在有幾個可以造成位移的召喚師技能(必須要是0cd狀態)
    int ShiftPlayerSkillCount() {
        int count = 0;
        for (int i = 0; i < psi.Length; i++) {
            PlayerSkillHandler psh = psi[i].GetComponent<PlayerSkillHandler>();
            if (psh != null) {
                if (psh.playerSkillID == (int)PlayerSkillID.Flash && psh.nowCD <= actionPoints) //閃現
                    count++;
                if (psh.playerSkillID == (int)PlayerSkillID.Ghost && psh.nowCD <= actionPoints) //鬼步
                    count++;
            }
        }
        return count;
    }

    List<Position> FindHeroCanMovePosition(HeroHandler hero) {
        List<Position> heroCanMovePositionList = new List<Position>();
        if (HeroCanMove(hero)) {
            bc.UpdateBoardHeroActionArrayXY(ActionType.HeroMove, hero.PositionXY[0], hero.PositionXY[1]);
            for (int mY = 0; mY < chessboardY; mY++)
                for (int mX = 0; mX < chessboardX; mX++)
                    if (bc.boardHeroActionArrayXY[mX, mY, (int)BoardHeroActionArrayItem.BoardHeroAction] == (int)BoardHeroAction.CanMove)
                        heroCanMovePositionList.Add(new Position(mX, mY));
        }
        return heroCanMovePositionList;
    }

    List<Position> FindHeroCanSkillPosition(HeroHandler hero) {
        List<Position> heroCanSkillPositionList = new List<Position>();
        if (HeroCanSkill(hero)) {
            bc.UpdateBoardHeroActionArrayXY(ActionType.HeroSkill, hero.PositionXY[0], hero.PositionXY[1]);
            for (int sY = 0; sY < chessboardY; sY++)
                for (int sX = 0; sX < chessboardX; sX++)
                    if (bc.boardHeroActionArrayXY[sX, sY, (int)BoardHeroActionArrayItem.BoardHeroAction] == (int)BoardHeroAction.CanSkill)
                        heroCanSkillPositionList.Add(new Position(sX, sY));
        }
        return heroCanSkillPositionList;
    }

    void Debug_PlayerSkill(int score, PlayerSkillHandler playerskill, HeroHandler targetHH) {
        Debug.Log(string.Format("{0} : ({1},{2}). Score : {3}", playerskill.playerSkillDataItem.name, targetHH.PositionXY[0], targetHH.PositionXY[1], score));
    }

    //已經召喚上場的英雄仍然會在HeroLockArray裡， 我們要移除他
    //heroLockArray有已經解鎖的英雄ID，但包含已經上場的英雄
    //waitingHeroIDList只有等待區的英雄，但包含還未解鎖的英雄ID
    List<int> GenUnLockedHeroList(List<int> unLockedHeroIDList, List<int> waitHeroIDList) {
        //存在於等待區，而且已經被解鎖的單位
        List<int> canCallHeroList = new List<int>();

        foreach(int waitingID in waitHeroIDList) {
            foreach(int unlockID in unLockedHeroIDList) {
                if(waitingID == unlockID) {
                    canCallHeroList.Add(waitingID);
                }
            }
        }

        return canCallHeroList;
    }

    bool IsHeroCanBeCalled(int heroID) {
        foreach(int unlockWaitingID in aUnLockedWaitListID) {
            if(heroID == unlockWaitingID) {
                return true;
            }
        }
        return false;
    }

    bool IsInNexusAttackRange(Position nexusPos, Position targetPos) {
        if (targetPos.x == nexusPos.x + 1 && targetPos.y == nexusPos.y) {
            return true;
        }
        if (targetPos.x == nexusPos.x && targetPos.y == nexusPos.y + 1) {
            return true;
        }
        if (targetPos.x == nexusPos.x + 1 && targetPos.y == nexusPos.y + 1) {
            return true;
        }
        if (targetPos.x == nexusPos.x - 1 && targetPos.y == nexusPos.y) {
            return true;
        }
        if (targetPos.x == nexusPos.x && targetPos.y == nexusPos.y - 1) {
            return true;
        }
        if (targetPos.x == nexusPos.x - 1 && targetPos.y == nexusPos.y - 1) {
            return true;
        }
        if (targetPos.x == nexusPos.x + 2 && targetPos.y == nexusPos.y) {
            return true;
        }
        if (targetPos.x == nexusPos.x + 2 && targetPos.y == nexusPos.y + 1) {
            return true;
        }
        if (targetPos.x == nexusPos.x + 2 && targetPos.y == nexusPos.y + 2) {
            return true;
        }
        if (targetPos.x == nexusPos.x + 1 && targetPos.y == nexusPos.y + 2) {
            return true;
        }
        if (targetPos.x == nexusPos.x && targetPos.y == nexusPos.y + 2) {
            return true;
        }
        if (targetPos.x == nexusPos.x - 1 && targetPos.y == nexusPos.y + 1) {
            return true;
        }
        if (targetPos.x == nexusPos.x - 2 && targetPos.y == nexusPos.y) {
            return true;
        }
        if (targetPos.x == nexusPos.x - 2 && targetPos.y == nexusPos.y - 1) {
            return true;
        }
        if (targetPos.x == nexusPos.x - 2 && targetPos.y == nexusPos.y - 2) {
            return true;
        }
        if (targetPos.x == nexusPos.x - 1 && targetPos.y == nexusPos.y - 2) {
            return true;
        }
        if (targetPos.x == nexusPos.x && targetPos.y == nexusPos.y - 2) {
            return true;
        }
        if (targetPos.x == nexusPos.x + 1 && targetPos.y == nexusPos.y - 1) {
            return true;
        }

        return false;
    }

    //進場到這個位置需消耗多少行動點?
    static int ActionPointToXY(Position pos) {
        //if ((StageData.GameMode)bc.stageDataItem.mode == StageData.GameMode.General)
        //    return 0;
        if (bc.p2BuildingHandlerList.Count != 0) {
            if (bc.nowGameTeam == GameTeam.A && bc.dist(pos.x * 9 + pos.y, bc.p2BuildingHandlerList[0].PositionXY[0] * 9 + bc.p2BuildingHandlerList[0].PositionXY[1]) <= 3) {
                return 3;
            }
        }
        if (bc.p1BuildingHandlerList.Count != 0) {
            if (bc.nowGameTeam == GameTeam.B && bc.dist(pos.x * 9 + pos.y, bc.p1BuildingHandlerList[0].PositionXY[0] * 9 + bc.p1BuildingHandlerList[0].PositionXY[1]) <= 3) {
                return 3;
            }
        }
            return 2;
    }

    //計算這個隊伍在場上有幾隻英雄 (不含召喚物)
    int NowHeroOnChessboard(GameTeam team = GameTeam.None) {
        int count = 0;
        List<HeroHandler> heroList = null;
        
        if (team == GameTeam.A) {
            heroList = bc.p1HeroHandlerList;
        }else if (team == GameTeam.B) {
            heroList = bc.p2HeroHandlerList;
        }else if (team == GameTeam.None) { //None代表不分隊計算場上的英雄
            heroList = bc.allHeroHandlerList;
        }

        for (int i = 0; i < heroList.Count; i++) {
            if (heroList[i].isSummon == 0) { //是否為召喚物
                count++;
            }
        }

        return count;
    }
    //void Update() {
    //    if (Input.GetKeyDown(KeyCode.G)) {
    //        if (isAIRunning == false) {
    //            isAIRunning = true;
    //            StartCoroutine(StartAI());
    //        } else {
    //            isAIRunning = false;
    //            StopAllCoroutines();
    //        }
    //    }

    //    if (Input.GetKeyDown(KeyCode.F)) {
    //        Action flashTest = new Action(ActionType.UseSummonerSkill, new int[] { (int)PlayerSkillID.Flash, 3, 3 }, new int[] { 1, 3 });
    //        StartCoroutine(AIFakeMove(new Action[] { flashTest }));
    //    }
    //}

    #endregion

}

