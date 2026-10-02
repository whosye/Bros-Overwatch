using TMPro;
using UnityEngine;
using UnityEngine.UI;

// HUD rezimu dobyvani bodu: nahore pod skore stav bodu (zamceny / volny / kdo ho zabira / sporny) a postup obou tymu,
// a ve svete znacka bodu se vzdalenosti, aby se dal najit.
public class CaptureUI
{
    const float BarWidth = 300f;

    GameObject panel;
    TextMeshProUGUI status;
    readonly Image[] fills = new Image[2];
    readonly TextMeshProUGUI[] percents = new TextMeshProUGUI[2];

    // zvuky: tikani pri zabirani, znelka pri odemceni
    float nextTick;
    int lastState = -1;
    int lastPoint = -1;

    RectTransform canvasRect;
    RectTransform marker;
    Image markerIcon;
    TextMeshProUGUI markerText;

    public void Build(Transform canvas)
    {
        canvasRect = (RectTransform)canvas;
        var top = new Vector2(0.5f, 1f);
        var center = new Vector2(0.5f, 0.5f);

        var panelImage = UiKit.MakeImage(canvas, "CapturePanel", new Color(0f, 0f, 0f, 0f), top, new Vector2(0f, -86f), new Vector2(900f, 90f));
        panel = panelImage.gameObject;

        status = UiKit.MakeText(panel.transform, "Status", "", 30, TextAlignmentOptions.Center, top, new Vector2(0f, 0f), new Vector2(900f, 40f));

        for (int team = 0; team < 2; team++)
        {
            float direction = team == 0 ? -1f : 1f;
            var background = UiKit.MakeImage(panel.transform, "Bar", new Color(0.05f, 0.07f, 0.10f, 0.75f), top,
                new Vector2(direction * (BarWidth * 0.5f + 44f), -50f), new Vector2(BarWidth + 6f, 24f));

            // Tym 0 se plni zprava doleva (od stredu), tym 1 zleva doprava.
            var anchor = new Vector2(team == 0 ? 1f : 0f, 0.5f);
            fills[team] = UiKit.MakeImage(background.transform, "Fill", UiKit.TeamColor(team), anchor, new Vector2(team == 0 ? -3f : 3f, 0f), new Vector2(0f, 18f));

            percents[team] = UiKit.MakeText(panel.transform, "Percent", "", 26, TextAlignmentOptions.Center, top,
                new Vector2(direction * 20f, -47f), new Vector2(80f, 30f), UiKit.TeamColor(team));
            percents[team].rectTransform.anchoredPosition = new Vector2(direction * (BarWidth + 80f), -50f);
        }

        // Znacka bodu ve svete.
        var markerObject = new GameObject("CaptureMarker", typeof(RectTransform));
        markerObject.transform.SetParent(canvas, false);
        marker = (RectTransform)markerObject.transform;
        marker.anchorMin = marker.anchorMax = center;
        marker.pivot = new Vector2(0.5f, 0f);
        markerIcon = UiKit.MakeImage(marker, "Icon", Color.white, new Vector2(0.5f, 0f), new Vector2(0f, 26f), new Vector2(22f, 22f));
        markerIcon.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 45f);
        markerText = UiKit.MakeText(marker, "Text", "", 22, TextAlignmentOptions.Center, new Vector2(0.5f, 0f), Vector2.zero, new Vector2(240f, 26f));

        panel.SetActive(false);
        markerObject.SetActive(false);
    }

    public void Tick(PlayerHero local, bool playing, MatchManager match)
    {
        if (panel == null) return;

        bool show = playing && match != null && match.IsCapture;
        if (panel.activeSelf != show)
            panel.SetActive(show);

        if (!show)
        {
            if (marker.gameObject.activeSelf)
                marker.gameObject.SetActive(false);
            return;
        }

        int state = match.pointState.Value;
        string point = $"BOD {match.pointIndex.Value + 1}/{MatchManager.CapturePointCount}";
        switch (state)
        {
            case MatchManager.StateLocked:
                status.text = $"{point}  ·  odemkne se za {Mathf.CeilToInt(match.lockRemaining.Value)} s";
                break;
            case MatchManager.StateFree:
                status.text = $"{point}  ·  VOLNÝ – vstup do něj";
                break;
            case MatchManager.StateContested:
                status.text = $"{point}  ·  SPORNÝ – vyřaď soupeře";
                break;
            default:
                status.text = $"{point}  ·  zabírá TÝM {(state == MatchManager.StateTeam0 ? 0 : 1)}";
                break;
        }
        status.color = state == MatchManager.StateLocked ? new Color(0.85f, 0.87f, 0.92f) : CapturePointView.StateColor(state);

        float[] progress = { match.progress0.Value, match.progress1.Value };
        for (int team = 0; team < 2; team++)
        {
            fills[team].rectTransform.sizeDelta = new Vector2(BarWidth * Mathf.Clamp01(progress[team]), 18f);
            percents[team].text = $"{Mathf.FloorToInt(progress[team] * 100f)} %";
        }

        UpdateMarker(local, match, state);
        UpdateSounds(local, match, state);
    }

    void UpdateSounds(PlayerHero local, MatchManager match, int state)
    {
        var fpc = local != null ? local.GetComponent<FirstPersonController>() : null;
        Vector3 ear = fpc != null && fpc.playerCamera != null ? fpc.playerCamera.transform.position : match.pointPosition.Value;

        // Bod se prave odemkl.
        bool samePoint = lastPoint == match.pointIndex.Value;
        if (samePoint && lastState == MatchManager.StateLocked && state != MatchManager.StateLocked)
            ProceduralSfx.Play(ProceduralSfx.CaptureUnlock, ear, 0.8f);

        lastState = state;
        lastPoint = match.pointIndex.Value;

        // Nekdo bod zabira: tikani, ktere se s postupem zrychluje.
        if (state != MatchManager.StateTeam0 && state != MatchManager.StateTeam1) return;
        if (Time.unscaledTime < nextTick) return;

        float progress = state == MatchManager.StateTeam0 ? match.progress0.Value : match.progress1.Value;
        nextTick = Time.unscaledTime + Mathf.Lerp(0.6f, 0.22f, progress);
        ProceduralSfx.Play(ProceduralSfx.CaptureTick, ear, 0.55f);
    }

    void UpdateMarker(PlayerHero local, MatchManager match, int state)
    {
        bool visible = false;
        var fpc = local != null ? local.GetComponent<FirstPersonController>() : null;
        var camera = fpc != null ? fpc.playerCamera : null;

        if (camera != null)
        {
            Vector3 world = match.pointPosition.Value + Vector3.up * 2.6f;
            Vector3 screen = camera.WorldToScreenPoint(world);
            float distance = Vector3.Distance(local.transform.position, match.pointPosition.Value);

            // Kdyz hrac v bodu stoji, znacka nad nim neprekazi.
            if (screen.z > 0.5f && distance > 6f
                && RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screen, null, out Vector2 point))
            {
                visible = true;
                marker.anchoredPosition = point;
                var color = CapturePointView.StateColor(state);
                markerIcon.color = color;
                markerText.color = color;
                markerText.text = $"{Mathf.RoundToInt(distance)} m";
            }
        }

        if (marker.gameObject.activeSelf != visible)
            marker.gameObject.SetActive(visible);
    }
}
