using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace Arcade.Compose
{
    // Runs after TMP's pointer handlers and replaces its time-only multi-click
    // selection with one consistent policy for the object editor's single-line fields.
    [DisallowMultipleComponent]
    [RequireComponent(typeof(TMP_InputField))]
    public sealed class AdeTextSelection : MonoBehaviour, IPointerDownHandler,
        IDragHandler, IPointerUpHandler, IDeselectHandler
    {
        private TMP_InputField input;
        private bool selectingAll;
        private float lastClickTime = float.NegativeInfinity;
        private Vector2 lastClickPosition;
        private int lastClickCaret = -1;
        private int clickCount;
        private int previousAnchor;
        private bool selectingCoordinate;
        private int coordinateAnchor, coordinateFocus;
        private Vector2 pointerDownPosition;
        private int pointerDownAnchor;
        private bool pointerSelectionActive, characterDragging;
        private bool normalizesCommit;
        private bool pointerFocusEstablished;

        public static void Configure(TMP_InputField field)
        {
            field.onFocusSelectAll = false;
            // Keep IME punctuation intact while typing; validate the normalized
            // value at commit instead of silently rejecting it character by character.
            field.characterValidation = TMP_InputField.CharacterValidation.None;
            if (!field.TryGetComponent<AdeTextSelection>(out _))
                field.gameObject.AddComponent<AdeTextSelection>();
        }

        public static string NormalizeForCommit(TMP_InputField field)
        {
            string normalized = field.text.Replace('；', ';').Replace('，', ',')
                .Replace('。', '.').Replace('．', '.');
            field.SetTextWithoutNotify(normalized);
            return normalized;
        }

        public static void ConfigureSettings(TMP_InputField field, bool numericOrGrid)
        {
            Configure(field);
            var selection = field.GetComponent<AdeTextSelection>();
            if (!numericOrGrid || selection.normalizesCommit) return;
            selection.normalizesCommit = true;
            var handlers = field.onEndEdit;
            field.onEndEdit = new TMP_InputField.SubmitEvent();
            field.onEndEdit.AddListener(_ => handlers.Invoke(NormalizeForCommit(field)));
        }

        private void Awake() => input = GetComponent<TMP_InputField>();

        public void OnPointerDown(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left) return;
            selectingAll = false;
            selectingCoordinate = false;
            pointerSelectionActive = characterDragging = false;
            if (!input.IsActive() || !input.IsInteractable()
                || input.lineType != TMP_InputField.LineType.SingleLine) return;

            // InputSystemUIInputModule increments eventData.clickCount on release,
            // so count presses here rather than treating its previous count as current.
            float now = Time.unscaledTime;
            float tolerance = EventSystem.current ? EventSystem.current.pixelDragThreshold : 5;
            int position = GetPointerCaret(eventData);
            // Distant blank pixels can resolve to the same text endpoint. Treat
            // that as the same click target, independently of pointer distance.
            clickCount = now - lastClickTime <= 0.3f
                && (position == lastClickCaret
                    || (eventData.position - lastClickPosition).sqrMagnitude <= tolerance * tolerance)
                ? clickCount + 1 : 1;
            lastClickTime = now;
            lastClickPosition = eventData.position;
            lastClickCaret = position;

            int character = TMP_TextUtilities.FindIntersectingCharacter(input.textComponent,
                eventData.position, eventData.pressEventCamera, true);
            bool blank = character < 0
                || char.IsWhiteSpace(input.textComponent.textInfo.characterInfo[character].character);
            if (blank && input.text.Length > 0)
            {
                character = TMP_TextUtilities.FindNearestCharacter(input.textComponent,
                    eventData.position, eventData.pressEventCamera, true);
                blank = character < 0;
            }
            bool shift = Keyboard.current != null && Keyboard.current.shiftKey.isPressed;
            pointerDownPosition = eventData.position;
            pointerDownAnchor = shift ? previousAnchor : position;
            pointerSelectionActive = true;
            bool firstClick = !pointerFocusEstablished;
            pointerFocusEstablished = true;
            selectingAll = !shift && (clickCount >= 3
                || (clickCount == 2 && blank));
            if (selectingAll) SelectWholeField();
            else if (clickCount == 2 && !shift && character >= 0)
            {
                SelectCoordinatePart(input.textComponent.textInfo.characterInfo[character].index);
            }
            else if (firstClick && !shift && input.text.Length > 0)
            {
                int index = character >= 0 ? input.textComponent.textInfo.characterInfo[character].index : position;
                var fragment = NearestFragment(input.text, index);
                coordinateAnchor = fragment.x; coordinateFocus = fragment.y;
                selectingCoordinate = true;
                SetSelection(fragment.x, fragment.y);
            }
            else
            {
                SetSelection(shift ? previousAnchor : position, position);
            }
        }

        private int GetPointerCaret(PointerEventData eventData)
        {
            if (input.text.Length == 0) return 0;
            int cursor = TMP_TextUtilities.GetCursorIndexFromPosition(input.textComponent,
                eventData.position, eventData.pressEventCamera, out CaretPosition side);
            var glyph = input.textComponent.textInfo.characterInfo[cursor];
            return Mathf.Clamp(glyph.index + (side == CaretPosition.Right ? glyph.stringLength : 0),
                0, input.text.Length);
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left || !pointerSelectionActive
                || !input.IsActive() || !input.IsInteractable()) return;
            float threshold = eventData.useDragThreshold && EventSystem.current
                ? EventSystem.current.pixelDragThreshold : 0;
            if (!characterDragging)
            {
                if ((eventData.position - pointerDownPosition).sqrMagnitude < threshold * threshold)
                {
                    ApplyHeldSelection();
                    return;
                }
                // Movement takes precedence over double/triple-click selection.
                // Restore the actual press caret, not the word/all-selection anchor.
                selectingAll = selectingCoordinate = false;
                characterDragging = true;
                SetSelection(pointerDownAnchor, GetPointerCaret(eventData));
            }
            // A drag is a separate gesture, never the first half of a double click.
            clickCount = 0;
            lastClickTime = float.NegativeInfinity;
            lastClickCaret = -1;
            // Subsequent drag updates and out-of-bounds scrolling remain owned by TMP.
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left) return;
            ApplyHeldSelection();
            selectingAll = false;
            selectingCoordinate = false;
            pointerSelectionActive = characterDragging = false;
        }

        // TMP can continue its own drag coroutine outside the field bounds.
        private void LateUpdate()
        {
            if (input.isFocused) ApplyHeldSelection();
            previousAnchor = input.selectionStringAnchorPosition;
        }

        private void Update()
        {
            if (Keyboard.current != null && Keyboard.current.anyKey.wasPressedThisFrame) ResetClicks();
        }

        private void OnApplicationFocus(bool focused) { if (!focused) { pointerFocusEstablished = false; ResetClicks(); } }

        internal static Vector2Int NearestFragment(string text, int index)
        {
            int best = int.MaxValue;
            var result = Vector2Int.zero;
            bool Separator(char c) => char.IsWhiteSpace(c) || c == '.' || c == '．' || c == '。'
                || c == ',' || c == '，' || c == ';' || c == '；';
            for (int i = 0; i < text.Length;)
            {
                if (Separator(text[i])) { i++; continue; }
                int start = i++;
                while (i < text.Length && !Separator(text[i])) i++;
                int distance = index < start ? start - index : index >= i ? index - (i - 1) : 0;
                if (distance >= best) continue;
                best = distance; result = new Vector2Int(start, i);
            }
            return result;
        }

        private void SelectWholeField()
        {
            SetSelection(0, input.text.Length);
        }

        private void ApplyHeldSelection()
        {
            if (selectingAll) SelectWholeField();
            else if (selectingCoordinate) SetSelection(coordinateAnchor, coordinateFocus);
        }

        private void SelectCoordinatePart(int index)
        {
            string text = input.text;
            if (text.Length == 0) return;
            index = Mathf.Clamp(index, 0, text.Length - 1);
            // Spaces around the comma belong to the nearest actual numeric fragment.
            if (char.IsWhiteSpace(text[index]) || text[index] == ',' || text[index] == '，')
            {
                for (int distance = 1; distance < text.Length; distance++)
                {
                    if (index - distance >= 0 && char.IsDigit(text[index - distance]))
                    { index -= distance; break; }
                    if (index + distance < text.Length && char.IsDigit(text[index + distance]))
                    { index += distance; break; }
                }
            }
            int start = index, end = index + 1;
            // Commas separate values; signs and decimal points stay inside the word.
            while (start > 0 && text[start - 1] != ',' && text[start - 1] != '，') start--;
            while (end < text.Length && text[end] != ',' && text[end] != '，') end++;
            while (start < end && char.IsWhiteSpace(text[start])) start++;
            while (end > start && char.IsWhiteSpace(text[end - 1])) end--;
            coordinateAnchor = start;
            coordinateFocus = end;
            selectingCoordinate = true;
            SetSelection(start, end);
        }

        private void SetSelection(int anchor, int focus)
        {
            input.selectionStringAnchorPosition = anchor;
            input.selectionStringFocusPosition = focus;
            input.ForceLabelUpdate();
        }

        public void OnDeselect(BaseEventData eventData) { pointerFocusEstablished = false; ResetClicks(); }
        private void OnDisable() { pointerFocusEstablished = false; ResetClicks(); }
        private void ResetClicks()
        {
            selectingAll = false;
            selectingCoordinate = false;
            pointerSelectionActive = characterDragging = false;
            clickCount = 0;
            lastClickTime = float.NegativeInfinity;
            lastClickCaret = -1;
        }
    }
}
