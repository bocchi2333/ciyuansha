using System;
using CiyuanSha.Gameplay.Cards;
using Godot;

namespace CiyuanSha.UI;

/// <summary>
/// Lightweight procedural card ornamentation drawn over a normal Godot Button.
/// It keeps all existing input behavior while making hand cards feel like paper cards.
/// </summary>
public partial class InkCardButton : Button
{
	private CardType _cardType = CardType.None;
	private CardSuit _suit = CardSuit.None;
	private int _seed = 17;
	private bool _selected;
	private bool _disabled;
	private bool _isVirtual;
	private float _pulseTime;

	public void Configure(CardInstance card, bool selected, bool disabled)
	{
		_cardType = card.CardType;
		_suit = card.Suit;
		_seed = HashCode.Combine(card.InstanceId, card.CardType, card.Suit, card.Rank);
		_selected = selected;
		_disabled = disabled;
		_isVirtual = false;
		SetProcess(_selected);
		QueueRedraw();
	}

	public void ConfigureVirtual(CardType cardType, string displayName, string subtitle, bool selected, bool disabled)
	{
		_cardType = cardType;
		_suit = CardSuit.None;
		_seed = HashCode.Combine(cardType, displayName, subtitle);
		_selected = selected;
		_disabled = disabled;
		_isVirtual = true;
		SetProcess(_selected);
		QueueRedraw();
	}

	public override void _Process(double delta)
	{
		if (!_selected)
		{
			return;
		}

		_pulseTime += (float)delta;
		QueueRedraw();
	}

	public override void _Draw()
	{
		if (Size.X < 24f || Size.Y < 32f)
		{
			return;
		}

		float alphaScale = _disabled ? 0.45f : 1f;
		Rect2 inner = new(new Vector2(7f, 7f), new Vector2(Size.X - 14f, Size.Y - 14f));
		Color accent = CyberStyle.GetCardAccent(_cardType);
		Color suitColor = CardRules.IsRedSuit(_suit)
			? new Color(0.58f, 0.07f, 0.045f, 0.34f * alphaScale)
			: new Color(0.055f, 0.045f, 0.035f, 0.34f * alphaScale);

		DrawPaperTint(inner, accent, alphaScale);
		DrawEdgeWear(inner, alphaScale);
		DrawPaperGrain(inner, alphaScale);
		DrawCenterSigil(inner, accent, alphaScale);
		DrawInkWash(inner, suitColor, alphaScale);
		DrawCornerBrackets(inner, accent, alphaScale);
		DrawCategoryBand(inner, accent, alphaScale);
		DrawSelectedGlint(inner, alphaScale);
	}

	private void DrawPaperTint(Rect2 rect, Color accent, float alphaScale)
	{
		DrawRect(rect.Grow(-2f), new Color(1f, 0.90f, 0.64f, 0.035f * alphaScale), true);
		Rect2 topWash = new(rect.Position + new Vector2(4f, 4f), new Vector2(rect.Size.X - 8f, rect.Size.Y * 0.24f));
		DrawRect(topWash, new Color(accent.R, accent.G, accent.B, (_isVirtual ? 0.070f : 0.045f) * alphaScale), true);
	}

	private void DrawEdgeWear(Rect2 rect, float alphaScale)
	{
		int state = _seed ^ 0x27d4eb2d;
		for (int index = 0; index < 24; index++)
		{
			bool vertical = index % 2 == 0;
			float side = Next01(ref state) > 0.5f ? 1f : 0f;
			float travel = Next01(ref state);
			Vector2 start = vertical
				? new Vector2(side > 0f ? rect.End.X : rect.Position.X, rect.Position.Y + rect.Size.Y * travel)
				: new Vector2(rect.Position.X + rect.Size.X * travel, side > 0f ? rect.End.Y : rect.Position.Y);
			Vector2 end = start + (vertical
				? new Vector2(NextSigned(ref state) * 5f, 3f + Next01(ref state) * 9f)
				: new Vector2(3f + Next01(ref state) * 9f, NextSigned(ref state) * 5f));
			Color color = index % 3 == 0
				? new Color(0.12f, 0.075f, 0.035f, 0.16f * alphaScale)
				: new Color(0.98f, 0.86f, 0.58f, 0.09f * alphaScale);
			DrawLine(start, end, color, 1f);
		}
	}

	private void DrawPaperGrain(Rect2 rect, float alphaScale)
	{
		int state = _seed;
		for (int index = 0; index < 18; index++)
		{
			float y = rect.Position.Y + Next01(ref state) * rect.Size.Y;
			float x = rect.Position.X + Next01(ref state) * rect.Size.X * 0.18f;
			float length = rect.Size.X * (0.42f + Next01(ref state) * 0.45f);
			Color color = index % 3 == 0
				? new Color(0.28f, 0.20f, 0.11f, 0.07f * alphaScale)
				: new Color(1f, 0.93f, 0.74f, 0.055f * alphaScale);
			DrawLine(new Vector2(x, y), new Vector2(Mathf.Min(rect.End.X - 5f, x + length), y + NextSigned(ref state) * 2.4f), color, 1f);
		}
	}

	private void DrawCenterSigil(Rect2 rect, Color accent, float alphaScale)
	{
		Vector2 center = rect.Position + rect.Size * 0.5f + new Vector2(0f, -5f);
		float radius = Mathf.Min(rect.Size.X, rect.Size.Y) * 0.23f;
		Color line = new(accent.R, accent.G, accent.B, (_isVirtual ? 0.20f : 0.13f) * alphaScale);
		DrawArc(center, radius, 0f, Mathf.Pi * 2f, 48, line, 1.2f, true);

		if (_cardType is CardType.FireSlash or CardType.FireAttack)
		{
			DrawLine(center + new Vector2(-radius * 0.30f, radius * 0.55f), center + new Vector2(-radius * 0.05f, -radius * 0.45f), line, 1.6f, true);
			DrawLine(center + new Vector2(-radius * 0.05f, -radius * 0.45f), center + new Vector2(radius * 0.20f, radius * 0.05f), line, 1.6f, true);
			DrawLine(center + new Vector2(radius * 0.20f, radius * 0.05f), center + new Vector2(radius * 0.48f, -radius * 0.55f), line, 1.6f, true);
		}
		else if (_cardType is CardType.ThunderSlash or CardType.Lightning)
		{
			DrawLine(center + new Vector2(-radius * 0.26f, -radius * 0.56f), center + new Vector2(radius * 0.08f, -radius * 0.08f), line, 1.8f, true);
			DrawLine(center + new Vector2(radius * 0.08f, -radius * 0.08f), center + new Vector2(-radius * 0.08f, radius * 0.08f), line, 1.8f, true);
			DrawLine(center + new Vector2(-radius * 0.08f, radius * 0.08f), center + new Vector2(radius * 0.32f, radius * 0.58f), line, 1.8f, true);
		}
		else if (CardRules.IsSlash(_cardType) || _cardType == CardType.Duel)
		{
			DrawLine(center + new Vector2(-radius * 0.70f, radius * 0.45f), center + new Vector2(radius * 0.72f, -radius * 0.50f), line, 2f, true);
			DrawLine(center + new Vector2(-radius * 0.30f, radius * 0.60f), center + new Vector2(radius * 0.86f, -radius * 0.18f), line, 1f, true);
		}
		else if (CardRules.IsEquipment(_cardType))
		{
			DrawLine(center + new Vector2(-radius * 0.72f, radius * 0.30f), center + new Vector2(radius * 0.72f, -radius * 0.30f), line, 1.8f, true);
			DrawLine(center + new Vector2(-radius * 0.18f, radius * 0.46f), center + new Vector2(radius * 0.18f, radius * 0.72f), line, 1.4f, true);
		}
		else
		{
			Rect2 seal = new(center - new Vector2(radius * 0.32f, radius * 0.46f), new Vector2(radius * 0.64f, radius * 0.92f));
			DrawRect(seal, line, false, 1.3f);
			DrawLine(seal.Position + new Vector2(4f, seal.Size.Y * 0.34f), new Vector2(seal.End.X - 4f, seal.Position.Y + seal.Size.Y * 0.34f), line, 1f);
			DrawLine(seal.Position + new Vector2(4f, seal.Size.Y * 0.66f), new Vector2(seal.End.X - 4f, seal.Position.Y + seal.Size.Y * 0.66f), line, 1f);
		}
	}

	private void DrawCornerBrackets(Rect2 rect, Color accent, float alphaScale)
	{
		Color line = new(accent.R, accent.G, accent.B, 0.52f * alphaScale);
		float left = rect.Position.X;
		float top = rect.Position.Y;
		float right = rect.End.X;
		float bottom = rect.End.Y;
		float length = 19f;
		float thickness = _selected ? 2.4f : 1.4f;

		DrawLine(new Vector2(left, top), new Vector2(left + length, top), line, thickness);
		DrawLine(new Vector2(left, top), new Vector2(left, top + length), line, thickness);
		DrawLine(new Vector2(right, top), new Vector2(right - length, top), line, thickness);
		DrawLine(new Vector2(right, top), new Vector2(right, top + length), line, thickness);
		DrawLine(new Vector2(left, bottom), new Vector2(left + length, bottom), line, thickness);
		DrawLine(new Vector2(left, bottom), new Vector2(left, bottom - length), line, thickness);
		DrawLine(new Vector2(right, bottom), new Vector2(right - length, bottom), line, thickness);
		DrawLine(new Vector2(right, bottom), new Vector2(right, bottom - length), line, thickness);
	}

	private void DrawCategoryBand(Rect2 rect, Color accent, float alphaScale)
	{
		Rect2 band = new(
			new Vector2(rect.Position.X + 5f, rect.End.Y - 27f),
			new Vector2(rect.Size.X - 10f, 18f));
		DrawRect(band, new Color(accent.R, accent.G, accent.B, 0.16f * alphaScale), true);
		DrawLine(band.Position, new Vector2(band.End.X, band.Position.Y), new Color(0.11f, 0.075f, 0.045f, 0.28f * alphaScale), 1f);
	}

	private void DrawInkWash(Rect2 rect, Color color, float alphaScale)
	{
		int state = _seed ^ 0x5f3759df;
		for (int index = 0; index < 5; index++)
		{
			float width = 14f + Next01(ref state) * 30f;
			float height = 4f + Next01(ref state) * 8f;
			float x = rect.Position.X + Next01(ref state) * (rect.Size.X - width);
			float y = rect.Position.Y + rect.Size.Y * (0.28f + Next01(ref state) * 0.36f);
			DrawRect(new Rect2(new Vector2(x, y), new Vector2(width, height)), new Color(color.R, color.G, color.B, color.A * alphaScale), true);
		}
	}

	private void DrawSelectedGlint(Rect2 rect, float alphaScale)
	{
		if (!_selected)
		{
			return;
		}

		float pulse = 0.22f + Mathf.Sin(_pulseTime * 5.2f) * 0.055f;
		Color gold = new(CyberStyle.Gold.R, CyberStyle.Gold.G, CyberStyle.Gold.B, pulse * alphaScale);
		DrawRect(rect.Grow(2f), gold, false, 3f);
		float x = rect.Position.X + rect.Size.X * (0.18f + (Mathf.Sin(_pulseTime * 2.1f) + 1f) * 0.26f);
		DrawLine(new Vector2(x, rect.Position.Y + 8f), new Vector2(x + rect.Size.X * 0.22f, rect.End.Y - 12f), new Color(1f, 0.90f, 0.58f, 0.20f * alphaScale), 2f, true);
	}

	private static float Next01(ref int state)
	{
		unchecked
		{
			state = state * 1103515245 + 12345;
		}

		return ((state >> 8) & 0xffff) / 65535f;
	}

	private static float NextSigned(ref int state)
	{
		return Next01(ref state) * 2f - 1f;
	}
}
