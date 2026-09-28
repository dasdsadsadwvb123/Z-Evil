using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 观察现有游戏状态的教学提示。只创建提示 UI，不接管输入、暂停、存档或关卡进度。
/// 自动在 game1/2/3 的玩家上创建，无需改场景或预制体。
/// </summary>
[DisallowMultipleComponent]
public class GameplayTutorial : MonoBehaviour
{
    private static bool bagLearned;
    private static bool notesLearned;
    private static bool meleeLearned;
    private static bool shotLearned;
    private static bool reloadLearned;

    private Inventory inventory;
    private InventoryUI inventoryUI;
    private PixelGridMovement movement;
    private HealthSystem health;
    private Gun gun;
    private PlayerPickup pickup;
    private DialogueManager[] dialogues;
    private DestructibleObstacle[] obstacles;
    private GameObject hintCanvas;
    private Text hintText;
    private RectTransform hintPanel;
    private string sceneName;
    private Vector2 lastPosition;
    private float walkedDistance;
    private bool usedDoor;
    private bool brokeObstacle;
    private bool sawReload;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetSession()
    {
        ResetProgress();
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private static void ResetProgress()
    {
        bagLearned = notesLearned = meleeLearned = shotLearned = reloadLearned = false;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Register()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name == "Intro") { ResetProgress(); return; }
        if (scene.name != "game1" && scene.name != "game2" && scene.name != "game3") return;
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            foreach (PixelGridMovement player in root.GetComponentsInChildren<PixelGridMovement>(true))
            {
                if (player.CompareTag("Player") && player.GetComponent<GameplayTutorial>() == null)
                    player.gameObject.AddComponent<GameplayTutorial>();
            }
        }
    }

    private void Start()
    {
        sceneName = gameObject.scene.name;
        inventory = GetComponent<Inventory>();
        inventoryUI = GetComponent<InventoryUI>();
        movement = GetComponent<PixelGridMovement>();
        health = GetComponent<HealthSystem>();
        gun = GetComponent<Gun>();
        pickup = GetComponent<PlayerPickup>();
        dialogues = FindObjectsOfType<DialogueManager>(true);
        obstacles = FindObjectsOfType<DestructibleObstacle>(true);
        lastPosition = transform.position;
        if (gun != null) gun.OnFired += OnFired;
        TeleportManager.OnAnyTeleport += OnTeleport;
        foreach (DestructibleObstacle obstacle in obstacles)
            if (obstacle.gameObject.scene == gameObject.scene && obstacle.onDestroyed != null)
                obstacle.onDestroyed.AddListener(OnObstacleDestroyed);
        CreateUI();
    }

    private bool CanShow()
    {
        if (inventory == null || movement == null || !movement.enabled || movement.frozen) return false;
        if (Time.timeScale <= 0f || (health != null && health.isDead)) return false;
        if (FadeController.IsTransitioning || SaveSystem.IsRestoring) return false;
        if (inventoryUI != null && inventoryUI.IsOpen) return false;
        if (NoteReaderUI.IsOpen || ContainerUI.IsOpen || PasswordPad.IsOpen
            || AllergyMatchUI.IsOpen || SimonPuzzleUI.IsOpen) return false;
        foreach (DialogueManager dialogue in dialogues)
            if (dialogue != null && dialogue.isPlaying) return false;
        return true;
    }

    private void LateUpdate()
    {
        if (inventoryUI != null && inventoryUI.IsOpen) bagLearned = true;
        if (inventoryUI != null && inventoryUI.IsOpen && NoteReaderUI.IsOpen) notesLearned = true;
        bool visible = CanShow();
        Vector2 position = transform.position;
        float distance = Vector2.Distance(position, lastPosition);
        // 只统计可操作时的小幅移动，出生吸附和传送不算完成移动教学。
        if (visible && distance < 1f) walkedDistance += distance;
        lastPosition = position;
        if (gun != null && visible)
        {
            if (gun.IsReloading) sawReload = true;
            else if (sawReload) { reloadLearned = true; sawReload = false; }
        }
        string message = visible ? GetHint() : null;
        hintCanvas.SetActive(!string.IsNullOrEmpty(message));
        if (message != null && hintText.text != message)
        {
            hintText.text = message;
            // 长句自动换行后按实际文字高度撑开，避免教学最后一行被裁掉。
            hintPanel.sizeDelta = new Vector2(640f, Mathf.Max(148f, hintText.preferredHeight + 24f));
        }
    }

    private string GetHint()
    {
        string interact = ControlHints.Key(pickup != null ? pickup.interactKey : KeyCode.F);
        string fire = ControlHints.Key(gun != null ? gun.fireKey : KeyCode.J);
        string bag = ControlHints.Key(inventoryUI != null ? inventoryUI.toggleKey : KeyCode.Tab);
        string use = ControlHints.Key(inventoryUI != null ? inventoryUI.useKey : KeyCode.Space);
        string journal = ControlHints.Key(inventoryUI != null ? inventoryUI.journalKey : KeyCode.Q);
        GunData equipped = inventory.GetEquippedGun();
        bool hasKnife = inventory.items.Exists(item => item != null && item.itemType == ItemType.Gun
            && item.gunData != null && item.gunData.range <= 1f);

        if (sceneName == "game1")
        {
            if (walkedDistance < 1.5f && !usedDoor && !hasKnife)
                return "<b>移动</b>\nWASD / 方向键移动，走近物体查看交互提示。";
            if (!usedDoor && !hasKnife)
                return "<b>开门与交互</b>\n走近门按 F；需要破开的门请反复按 F。\n查看、拾取等操作也会在靠近时显示提示。";
            if (!hasKnife && !meleeLearned)
                return "<b>拾取小刀</b>\n走近小刀按 " + interact + " 拾取，拾取后自动装备。";
            if (hasKnife && (equipped == null || equipped.range > 1f) && !meleeLearned)
                return "<b>装备小刀</b>\n" + bag + " 打开背包 → WASD 选小刀 → " + use + " 装备。";
            if (hasKnife && !brokeObstacle && HasActiveObstacle())
                return "<b>打碎障碍物</b>\n靠近并面朝障碍物，反复按 " + fire + " 挥刀。\n小刀有耐久；武器方向跟随人物朝向。";
            if (hasKnife && !meleeLearned)
                return "<b>使用小刀</b>\n面朝目标按 " + fire + " 挥刀，小刀有耐久。";
        }

        if (sceneName == "game2" && !bagLearned)
            return "<b>整理背包</b>\n按 " + bag + " 打开背包，WASD 选择，" + use + " 装备 / 草药操作。\n物品靠近按 " + interact + " 拾取；钥匙在开门时自动检查。";
        if (NoteJournal.Notes.Count > 0 && !notesLearned)
            return "<b>阅读已收集的纸条</b>\n" + bag + " 背包 → " + journal + " 纸条列表 → W/S 选择 → E 阅读。\nE / F / Esc 返回列表，Tab 关闭回游戏。";
        if (equipped != null && equipped.range > 1f)
        {
            if (!shotLearned)
                return "<b>使用枪械</b>\n面朝目标按 " + fire + " 射击。\n按 " + ControlHints.Key(gun != null ? gun.reloadKey : KeyCode.R) + " 换弹；需有对应的备用弹药。";
            if (!reloadLearned)
                return "<b>补充弹药</b>\n按 " + ControlHints.Key(gun != null ? gun.reloadKey : KeyCode.R) + " 换弹，换弹时暂时不能射击。\n在存档点，R 同时是读档键，请留意靠近时的提示。";
        }
        return null;
    }

    private bool HasActiveObstacle()
    {
        foreach (DestructibleObstacle obstacle in obstacles)
            if (obstacle != null && obstacle.gameObject.scene == gameObject.scene
                && obstacle.gameObject.activeInHierarchy && obstacle.GetHealth() > 0) return true;
        return false;
    }

    private void OnFired()
    {
        if (!CanShow()) return;
        GunData equipped = inventory.GetEquippedGun();
        if (equipped == null) return;
        if (equipped.range <= 1f) meleeLearned = true;
        else shotLearned = true;
    }

    private void OnTeleport() { if (!SaveSystem.IsRestoring) usedDoor = true; }
    private void OnObstacleDestroyed() { brokeObstacle = true; meleeLearned = true; }

    private void CreateUI()
    {
        hintCanvas = new GameObject("GameplayTutorialCanvas", typeof(RectTransform), typeof(Canvas));
        hintCanvas.transform.SetParent(transform, false);
        hintCanvas.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
        hintCanvas.GetComponent<Canvas>().sortingOrder = 90;
        UIScale.Setup(hintCanvas);
        GameObject panel = new GameObject("Hint", typeof(RectTransform), typeof(Image));
        panel.transform.SetParent(hintCanvas.transform, false);
        Image background = panel.GetComponent<Image>();
        background.color = new Color(0.04f, 0.04f, 0.05f, 0.84f);
        background.raycastTarget = false;
        RectTransform rect = panel.GetComponent<RectTransform>();
        hintPanel = rect;
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(24f, -24f);
        rect.sizeDelta = new Vector2(640f, 148f);
        GameObject label = new GameObject("Text", typeof(RectTransform), typeof(Text));
        label.transform.SetParent(panel.transform, false);
        hintText = label.GetComponent<Text>();
        hintText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        hintText.fontSize = 24;
        hintText.color = new Color(1f, 0.95f, 0.82f);
        hintText.alignment = TextAnchor.UpperLeft;
        hintText.supportRichText = true;
        hintText.raycastTarget = false;
        RectTransform labelRect = label.GetComponent<RectTransform>();
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = new Vector2(18f, 12f);
        labelRect.offsetMax = new Vector2(-18f, -12f);
        hintCanvas.SetActive(false);
    }

    private void OnDisable() { if (hintCanvas != null) hintCanvas.SetActive(false); }

    private void OnDestroy()
    {
        if (gun != null) gun.OnFired -= OnFired;
        TeleportManager.OnAnyTeleport -= OnTeleport;
        if (obstacles != null)
            foreach (DestructibleObstacle obstacle in obstacles)
                if (obstacle != null && obstacle.onDestroyed != null)
                    obstacle.onDestroyed.RemoveListener(OnObstacleDestroyed);
        if (hintCanvas != null) Destroy(hintCanvas);
    }
}
