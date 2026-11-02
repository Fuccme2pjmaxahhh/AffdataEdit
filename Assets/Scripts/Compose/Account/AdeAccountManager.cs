using Arcade.Util.UniTaskHelper;
using Cysharp.Threading.Tasks;
using System;
using System.Globalization;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Text = TMPro.TMP_Text;
using InputField = TMPro.TMP_InputField;

namespace Arcade.Compose
{
	public sealed class AdeAccountManager : MonoBehaviour
	{
		private const string LoginUrl = "https://api.affdata.net/v2/game/login";
		private const string EditorSceneName = "ArcEditor";
		private const string RightBarPath = "ArcaeaEditor/EditorCanvas/Bars/Right/View";

		private sealed class Session
		{
			public string Token;
			public string Username;
			public DateTime ExpiresAt;
		}

		public static AdeAccountManager Instance { get; private set; }

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void ResetStaticState() => Instance = null;

		public static bool IsLoggedIn => Instance != null && Instance.session != null && Instance.session.ExpiresAt > DateTime.UtcNow;
		public static string Token => IsLoggedIn ? Instance.session.Token : null;
		public static string Username => IsLoggedIn ? Instance.session.Username : null;

		private Session session;
		private bool loggingIn;
		private AdeDualDialog dialog;
		private GameObject usernameRow, pinRow;
		private InputField usernameInput, pinInput;
		private Text statusText, introductionText;
		public AdeDualDialog SettingsDialog => dialog;
		public void PrepareSettingsView()
		{
			SetStatus(null);
			UpdateView();
		}

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
		private static void Bootstrap()
		{
			SceneManager.sceneLoaded -= OnSceneLoaded;
			SceneManager.sceneLoaded += OnSceneLoaded;
		}

		private static void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
		{
			if (scene.name == EditorSceneName && Instance == null)
				new GameObject(nameof(AdeAccountManager)).AddComponent<AdeAccountManager>();
		}

		private void Awake()
		{
			Instance = this;
			LoadSession();
		}

		private void OnDestroy()
		{
			if (Instance == this) Instance = null;
		}

		private void Start()
		{
			GameObject rightBar = GameObject.Find(RightBarPath);
			if (rightBar != null) AdeUiKit.CreateIconButton(rightBar.transform, "Account", "Account", ShowLoginWindow);
			BuildDialog();
			UpdateView();
		}

		// Reuse the dialog shell and input controls, but give the account form its own layout.
		private void BuildDialog()
		{
			AdeDualDialog source = Feature.AdeObsManager.Instance != null ? Feature.AdeObsManager.Instance.OBSDialog : null;
			Transform inputRow = source != null ? AdeUiKit.FindRow(source, "Address") : null;
			if (inputRow == null)
			{
				Debug.LogWarning("Account dialog templates not found");
				return;
			}
			dialog = AdeUiKit.CloneDialog(source, "AccountDialog");
			dialog.Title.text = "AffdataNet";
			Transform content = AdeUiKit.Content(dialog);
            var form = content.GetComponent<VerticalLayoutGroup>();
            form.padding = new RectOffset(28, 28, 20, 24);
            form.spacing = 18;
            form.childControlWidth = form.childControlHeight = true;
            form.childForceExpandWidth = true;
            form.childForceExpandHeight = false;
            AdeUiKit.SetMaxHeight(dialog, 520);
            ((RectTransform)dialog.View.transform).SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, 600);
            introductionText = CreateFormText(content, inputRow, "登录账号，连接你的云端谱面", 22);

			usernameRow = AdeUiKit.CloneRow(inputRow, content, "Username");
			usernameRow.GetComponentInChildren<Text>(true).text = "用户名";
			usernameInput = usernameRow.GetComponentInChildren<InputField>(true);
			usernameInput.characterLimit = 64;
            usernameInput.text = "";
            StyleField(usernameRow, usernameInput, "输入 AffdataNet 用户名");

			pinRow = AdeUiKit.CloneRow(inputRow, content, "Pin");
			pinRow.GetComponentInChildren<Text>(true).text = "PIN";
			pinInput = pinRow.GetComponentInChildren<InputField>(true);
			pinInput.contentType = InputField.ContentType.Pin;
			pinInput.characterLimit = 6;
            pinInput.text = "";
            StyleField(pinRow, pinInput, "输入 6 位 PIN");
            CreateFormText(pinRow.transform, inputRow, "使用在 AffdataNet 网站设置的游戏 PIN", 18);
			pinInput.onEndEdit.AddListener(_ =>
			{
				if (Keyboard.current != null && (Keyboard.current.enterKey.isPressed || Keyboard.current.numpadEnterKey.isPressed))
					OnPrimary();
			});

			statusText = CreateFormText(content, inputRow, "", 20);

			AdeUiKit.SetOnClick(dialog.RightButton, OnPrimary);
			AdeUiKit.SetOnClick(dialog.LeftButton, dialog.Close);
			dialog.LeftButtonText.text = "取消";
			dialog.OnClose += () =>
			{
				if (pinInput != null) pinInput.text = "";
			};
		}

        private static Text CreateFormText(Transform parent, Transform template, string caption, int fontSize)
        {
            Text label = AdeUiKit.CreateLabelRow(parent, template.GetComponentInChildren<Text>(true).transform, caption);
            label.fontSize = fontSize;
            label.raycastTarget = false;
            label.transform.parent.GetComponent<HorizontalLayoutGroup>().padding = new RectOffset();
            label.GetComponent<LayoutElement>().preferredWidth = 480;
            return label;
        }

        private static void StyleField(GameObject row, InputField input, string placeholder)
        {
            DestroyImmediate(row.GetComponent<HorizontalLayoutGroup>());
            var layout = row.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 8;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            var label = row.transform.Find("Text").GetComponent<Text>();
            label.fontSize = 20;
            label.alignment = TMPro.TextAlignmentOptions.Left;
            label.raycastTarget = false;
            var size = input.GetComponent<LayoutElement>();
            size.minWidth = 0;
            size.preferredWidth = 480;
            size.preferredHeight = 48;
            input.textComponent.fontSize = 24;
            input.textComponent.alignment = TMPro.TextAlignmentOptions.Left;
            input.textViewport.offsetMin = new Vector2(14, 0);
            input.textViewport.offsetMax = new Vector2(-14, 0);
            input.textComponent.rectTransform.offsetMin = Vector2.zero;
            input.textComponent.rectTransform.offsetMax = Vector2.zero;
            var hint = Instantiate(input.textComponent, input.textComponent.transform.parent, false);
            hint.name = "Placeholder";
            hint.text = placeholder;
            hint.fontStyle = TMPro.FontStyles.Normal;
            hint.color = new Color(.55f, .55f, .55f, 1);
            hint.raycastTarget = false;
            input.placeholder = hint;
        }

		public void ShowLoginWindow()
		{
			if (AdeSettingsDialog.Instance && AdeSettingsDialog.Instance.Ready)
			{
				AdeSettingsDialog.Instance.Show(AdeSettingsDialog.Page.Account);
				return;
			}
			if (dialog == null) return;
			SetStatus(null);
			UpdateView();
			dialog.Open();
		}

		private void OnPrimary()
		{
			if (loggingIn) return;
			if (IsLoggedIn)
			{
				Logout();
				return;
			}
			if (usernameInput.text.Trim().Length == 0 || pinInput.text.Length != 6)
			{
				SetStatus("请输入用户名和 6 位 PIN");
				return;
			}
			Login().Forget();
		}

		private void SetStatus(string text)
		{
			if (statusText == null) return;
			if (text == null && IsLoggedIn)
				text = $"已登录：{session.Username}\n有效期至：{session.ExpiresAt.ToLocalTime():yyyy-MM-dd HH:mm}";
			statusText.text = text ?? "";
			statusText.transform.parent.gameObject.SetActive(!string.IsNullOrEmpty(statusText.text));
		}

		private void UpdateView()
		{
			if (dialog == null) return;
			usernameRow.SetActive(!IsLoggedIn);
			pinRow.SetActive(!IsLoggedIn);
			if (!IsLoggedIn && session == null && string.IsNullOrEmpty(usernameInput.text)) usernameInput.text = AdeSettings.GetAccountUsername();
			introductionText.text = IsLoggedIn ? "账号已连接，可以使用云端谱面" : "登录账号，连接你的云端谱面";
            usernameInput.interactable = pinInput.interactable = !loggingIn;
            dialog.LeftButtonText.text = IsLoggedIn ? "关闭" : "取消";
			dialog.RightButtonText.text = IsLoggedIn ? "退出登录" : (loggingIn ? "登录中…" : "登录");
			dialog.RightButton.interactable = !loggingIn;
		}

		private void LoadSession()
		{
			try
			{
				session = JsonConvert.DeserializeObject<Session>(AdeSettings.GetAccountSession());
			}
			catch (JsonException)
			{
				session = null;
			}
			if (session != null && (string.IsNullOrEmpty(session.Token) || session.ExpiresAt <= DateTime.UtcNow))
				session = null;
		}

		private bool SaveSession()
		{
			try
			{
				AdeSettings.SaveAccountSession(session == null ? null : JsonConvert.SerializeObject(session), session?.Username);
				UpdateView();
				return true;
			}
			catch (Exception error) when (error is IOException || error is UnauthorizedAccessException || error is JsonException)
			{
				SetStatus("无法保存登录状态：" + error.Message);
				return false;
			}
		}

		private async UniTask Login()
		{
			loggingIn = true;
            try
            {
				UpdateView();
				SetStatus("正在登录…");
				string payload = JsonConvert.SerializeObject(new { username = usernameInput.text.Trim(), password = pinInput.text });
				using (var request = new UnityWebRequest(LoginUrl, UnityWebRequest.kHttpVerbPOST))
				{
					request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(payload));
					request.downloadHandler = new DownloadHandlerBuffer();
					request.SetRequestHeader("Content-Type", "application/json");
					request.timeout = 15;
					await request.SendWithResultAsync(destroyCancellationToken);
					loggingIn = false;

					JObject body = null;
					try
					{
						body = JObject.Parse(request.downloadHandler.text ?? "");
					}
					catch (JsonException)
					{
					}

					string token = (string)body?["token"];
					string expiresAt = (string)body?["expiresAt"];
					if (request.result == UnityWebRequest.Result.Success && !string.IsNullOrEmpty(token)
						&& DateTime.TryParse(expiresAt, CultureInfo.InvariantCulture,
							DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out DateTime expires))
					{
						session = new Session
						{
							Token = token,
							Username = (string)body["user"]?["username"] ?? usernameInput.text.Trim(),
							ExpiresAt = expires
						};
						pinInput.text = "";
						if (!SaveSession()) { UpdateView(); return; }
						dialog.Close();
						AdeToast.Instance?.Show($"已登录 AffdataNet：{session.Username}");
					}
					else
					{
						UpdateView();
						string error = (string)body?["error"];
						SetStatus(request.responseCode == 423
							? "PIN 已锁定，请在 AffdataNet 网站通过邮箱验证后重置 PIN。"
							: "登录失败：" + (string.IsNullOrEmpty(error) ? request.error : error));
					}
				}
            }
            finally { loggingIn = false; }
		}

		private void Logout()
		{
			Session previous = session;
			session = null;
			if (!SaveSession()) { session = previous; UpdateView(); return; }
			SetStatus(null);
			AdeToast.Instance?.Show("已退出 AffdataNet 登录");
		}
	}
}
