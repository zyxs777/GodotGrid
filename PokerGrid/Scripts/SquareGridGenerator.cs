using System.Collections.Generic;
using Godot;

namespace Car.Scripts;

public partial class SquareGridGenerator : Node2D
{
	private const int PokerHandCardCount = 5;
	private const string GridContainerName = "GeneratedGrid";
	private const string UiCanvasName = "PokerHandUi";

	private enum CardSuit
	{
		Hearts,
		Diamonds,
		Clubs,
		Spades,
	}

	private enum PokerHandRank
	{
		HighCard,
		OnePair,
		TwoPair,
		ThreeOfAKind,
		Straight,
		Flush,
		FullHouse,
		FourOfAKind,
		StraightFlush,
		RoyalFlush,
	}

	private readonly struct PlayingCard
	{
		public PlayingCard(CardSuit suit, string rank, int rankValue)
		{
			Suit = suit;
			Rank = rank;
			RankValue = rankValue;
		}

		public CardSuit Suit { get; }
		public string Rank { get; }
		public int RankValue { get; }
	}

	private sealed class PokerHandMatch
	{
		public PokerHandMatch(PokerHandRank rank, Vector2I[] cells)
		{
			Rank = rank;
			Cells = cells;
		}

		public PokerHandRank Rank { get; }
		public Vector2I[] Cells { get; }
	}

	[Export]
	public PackedScene CellScene { get; set; }

	[Export]
	public int Columns { get; set; } = 6;

	[Export]
	public int Rows { get; set; } = 6;

	[Export]
	public Vector2 CellSize { get; set; } = Vector2.Zero;

	[Export]
	public bool CenterOnOrigin { get; set; } = true;

	[Export]
	public float CameraPadding { get; set; } = 1.15f;

	[Export]
	public Texture2D HeartSuitTexture { get; set; }

	[Export]
	public Texture2D DiamondSuitTexture { get; set; }

	[Export]
	public Texture2D ClubSuitTexture { get; set; }

	[Export]
	public Texture2D SpadeSuitTexture { get; set; }

	[Export]
	public Vector2 UiMargin { get; set; } = new(20.0f, 20.0f);

	[Export]
	public float UiWidth { get; set; } = 150.0f;

	private Vector2 _lastCellSize = Vector2.Zero;
	private PlayingCard[,] _gridCards;
	private GridCell[,] _gridCells;
	private readonly Dictionary<PokerHandRank, List<PokerHandMatch>> _matchesByRank = new();
	private readonly List<Button> _handButtons = new();
	private readonly Dictionary<PokerHandRank, Button> _rankButtons = new();
	private PokerHandRank? _selectedHandRank;
	private bool _isUpdatingFaceUpStates;

	public override void _Ready()
	{
		BuildGrid();
		FitCameraToGrid();
	}

	public void BuildGrid()
	{
		if (CellScene == null)
		{
			GD.PushWarning($"{Name} cannot build a grid because CellScene is not assigned.");
			return;
		}

		var grid = GetOrCreateContainer(GridContainerName);
		ClearHighlights();
		ClearChildren(grid);
		_selectedHandRank = null;
		_gridCards = null;
		_gridCells = null;
		_matchesByRank.Clear();

		var cellSize = ResolveCellSize();
		_lastCellSize = cellSize;
		var deck = CreateShuffledDeck();
		var tileCount = Columns * Rows;
		if (tileCount > deck.Count)
		{
			GD.PushWarning($"{Name} needs {tileCount} cards, but the poker deck only has {deck.Count} unique cards.");
			return;
		}

		var firstTilePosition = CenterOnOrigin
			? new Vector2(-(Columns - 1) * cellSize.X * 0.5f, -(Rows - 1) * cellSize.Y * 0.5f)
			: Vector2.Zero;
		var cardIndex = 0;
		_gridCards = new PlayingCard[Rows, Columns];
		_gridCells = new GridCell[Rows, Columns];

		for (var row = 0; row < Rows; row++)
		{
			for (var column = 0; column < Columns; column++)
			{
				var card = deck[cardIndex];
				var tilePosition = firstTilePosition + new Vector2(column * cellSize.X, row * cellSize.Y);
				var tile = CreateCellInstance(row, column, cellSize, card);
				tile.Position = tilePosition;

				grid.AddChild(tile);
				_gridCards[row, column] = card;
				_gridCells[row, column] = tile as GridCell;
				cardIndex++;
			}
		}

		CollectPokerHandMatches();
		BuildPokerHandUi();
	}

	private void FitCameraToGrid()
	{
		var camera = GetNodeOrNull<Camera2D>("Camera2D");
		if (camera == null || CellScene == null)
		{
			return;
		}

		var viewport = GetViewport();
		if (viewport == null)
		{
			return;
		}

		var viewportSize = viewport.GetVisibleRect().Size;
		if (viewportSize.X <= 0.0f || viewportSize.Y <= 0.0f)
		{
			return;
		}

		var cellSize = _lastCellSize.X > 0.0f && _lastCellSize.Y > 0.0f ? _lastCellSize : ResolveCellSize();
		var gridSize = new Vector2(Columns * cellSize.X, Rows * cellSize.Y);
		if (gridSize.X <= 0.0f || gridSize.Y <= 0.0f)
		{
			return;
		}

		camera.Enabled = true;
		camera.Position = CenterOnOrigin ? Vector2.Zero : (gridSize - cellSize) * 0.5f;

		var padding = Mathf.Max(CameraPadding, 1.0f);
		var zoom = Mathf.Min(viewportSize.X / gridSize.X, viewportSize.Y / gridSize.Y) / padding;
		camera.Zoom = Vector2.One * zoom;
	}

	private Node2D CreateCellInstance(int row, int column, Vector2 cellSize, PlayingCard card)
	{
		var cell = CellScene.Instantiate<Node2D>();
		cell.Name = $"Cell_{row}_{column}";

		if (cell is GridCell gridCell)
		{
			gridCell.CellSize = cellSize;
			gridCell.SetCard(GetSuitTexture(card.Suit), card.Rank, GetSuitColor(card.Suit));
			gridCell.FaceUpChanged += OnCellFaceUpChanged;
			gridCell.ApplyVisual();
		}

		return cell;
	}

	private Vector2 ResolveCellSize()
	{
		var sampleCell = CellScene.Instantiate<Node2D>();
		var sceneCellSize = GetCellSize(sampleCell);
		sampleCell.Free();

		return new Vector2(
			CellSize.X > 0.0f ? CellSize.X : sceneCellSize.X,
			CellSize.Y > 0.0f ? CellSize.Y : sceneCellSize.Y);
	}

	private static Vector2 GetCellSize(Node2D cell)
	{
		if (cell is GridCell gridCell)
		{
			gridCell.ApplyVisual();
			return gridCell.GetCellSize();
		}

		var sprite = cell.GetNodeOrNull<Sprite2D>("Sprite2D");
		return sprite?.Texture?.GetSize() ?? Vector2.Zero;
	}

	private Node2D GetOrCreateContainer(string containerName)
	{
		var container = GetNodeOrNull<Node2D>(containerName);
		if (container != null)
		{
			return container;
		}

		container = new Node2D
		{
			Name = containerName,
		};
		AddChild(container);
		return container;
	}

	private static void ClearChildren(Node node)
	{
		foreach (var child in node.GetChildren())
		{
			node.RemoveChild(child);
			child.Free();
		}
	}

	private void CollectPokerHandMatches()
	{
		_matchesByRank.Clear();

		if (_gridCards == null || _gridCells == null)
		{
			return;
		}

		var directions = new[]
		{
			(ColumnStep: 1, RowStep: 0),
			(ColumnStep: 0, RowStep: 1),
			(ColumnStep: 1, RowStep: 1),
			(ColumnStep: 1, RowStep: -1),
		};

		for (var row = 0; row < Rows; row++)
		{
			for (var column = 0; column < Columns; column++)
			{
				foreach (var direction in directions)
				{
					var endRow = row + direction.RowStep * (PokerHandCardCount - 1);
					var endColumn = column + direction.ColumnStep * (PokerHandCardCount - 1);
					if (!IsInsideGrid(endRow, endColumn))
					{
						continue;
					}

					var hand = new List<PlayingCard>(PokerHandCardCount);
					var cells = new Vector2I[PokerHandCardCount];
					var allFaceUp = true;
					for (var i = 0; i < PokerHandCardCount; i++)
					{
						var handRow = row + direction.RowStep * i;
						var handColumn = column + direction.ColumnStep * i;

						if (_gridCells[handRow, handColumn]?.IsFaceUp != true)
						{
							allFaceUp = false;
							break;
						}

						hand.Add(_gridCards[handRow, handColumn]);
						cells[i] = new Vector2I(handColumn, handRow);
					}

					if (!allFaceUp)
					{
						continue;
					}

					var handRank = EvaluatePokerHand(hand);
					if (handRank == PokerHandRank.HighCard)
					{
						continue;
					}

					AddPokerHandMatch(new PokerHandMatch(handRank, cells));
				}
			}
		}
	}

	private void RefreshPokerHandMatches()
	{
		CollectPokerHandMatches();
		UpdatePokerHandButtonTexts();

		if (_selectedHandRank.HasValue)
		{
			ShowMatches(_selectedHandRank.Value);
		}
		else
		{
			ClearHighlights();
		}
	}

	private void OnCellFaceUpChanged(GridCell cell)
	{
		if (_isUpdatingFaceUpStates)
		{
			return;
		}

		RefreshPokerHandMatches();
	}

	private bool IsInsideGrid(int row, int column)
	{
		return row >= 0 && row < Rows && column >= 0 && column < Columns;
	}

	private void AddPokerHandMatch(PokerHandMatch match)
	{
		if (!_matchesByRank.TryGetValue(match.Rank, out var matches))
		{
			matches = new List<PokerHandMatch>();
			_matchesByRank[match.Rank] = matches;
		}

		matches.Add(match);
	}

	private void BuildPokerHandUi()
	{
		var existingCanvas = GetNodeOrNull<CanvasLayer>(UiCanvasName);
		if (existingCanvas != null)
		{
			RemoveChild(existingCanvas);
			existingCanvas.Free();
		}

		_handButtons.Clear();
		_rankButtons.Clear();

		var canvas = new CanvasLayer
		{
			Name = UiCanvasName,
		};
		AddChild(canvas);

		var panel = new PanelContainer
		{
			Name = "Panel",
			AnchorLeft = 1.0f,
			AnchorRight = 1.0f,
			AnchorTop = 0.0f,
			AnchorBottom = 0.0f,
			OffsetLeft = -UiWidth - UiMargin.X,
			OffsetRight = -UiMargin.X,
			OffsetTop = UiMargin.Y,
			OffsetBottom = UiMargin.Y + 460.0f,
		};
		canvas.AddChild(panel);

		var buttonList = new VBoxContainer
		{
			Name = "HandButtons",
		};
		panel.AddChild(buttonList);

		var clearButton = new Button
		{
			Name = "ClearHighlightButton",
			Text = "\u53d6\u6d88\u9ad8\u4eae",
			CustomMinimumSize = new Vector2(UiWidth - 24.0f, 34.0f),
			FocusMode = Control.FocusModeEnum.None,
		};
		clearButton.Pressed += ClearSelectedHand;
		buttonList.AddChild(clearButton);
		_handButtons.Add(clearButton);

		var shuffleButton = new Button
		{
			Name = "ShuffleButton",
			Text = "\u91cd\u65b0\u968f\u673a",
			CustomMinimumSize = new Vector2(UiWidth - 24.0f, 34.0f),
			FocusMode = Control.FocusModeEnum.None,
		};
		shuffleButton.Pressed += BuildGrid;
		buttonList.AddChild(shuffleButton);
		_handButtons.Add(shuffleButton);

		var flipAllButton = new Button
		{
			Name = "FlipAllButton",
			Text = "\u5168\u90e8\u7ffb\u9762",
			CustomMinimumSize = new Vector2(UiWidth - 24.0f, 34.0f),
			FocusMode = Control.FocusModeEnum.None,
		};
		flipAllButton.Pressed += FlipAllCards;
		buttonList.AddChild(flipAllButton);
		_handButtons.Add(flipAllButton);

		foreach (var handRank in GetSelectableHandRanks())
		{
			var button = new Button
			{
				Name = $"{handRank}Button",
				CustomMinimumSize = new Vector2(UiWidth - 24.0f, 34.0f),
				FocusMode = Control.FocusModeEnum.None,
			};
			button.Pressed += () => ShowMatches(handRank);

			buttonList.AddChild(button);
			_handButtons.Add(button);
			_rankButtons[handRank] = button;
		}

		UpdatePokerHandButtonTexts();
	}

	private void ShowMatches(PokerHandRank handRank)
	{
		_selectedHandRank = handRank;
		ClearHighlights();

		if (!_matchesByRank.TryGetValue(handRank, out var matches))
		{
			return;
		}

		foreach (var match in matches)
		{
			foreach (var cellPosition in match.Cells)
			{
				if (!IsInsideGrid(cellPosition.Y, cellPosition.X))
				{
					continue;
				}

				_gridCells[cellPosition.Y, cellPosition.X]?.SetHighlighted(true);
			}
		}
	}

	private void ClearSelectedHand()
	{
		_selectedHandRank = null;
		ClearHighlights();
	}

	private void ClearHighlights()
	{
		if (_gridCells == null)
		{
			return;
		}

		for (var row = 0; row < _gridCells.GetLength(0); row++)
		{
			for (var column = 0; column < _gridCells.GetLength(1); column++)
			{
				_gridCells[row, column]?.SetHighlighted(false);
			}
		}
	}

	private int GetMatchCount(PokerHandRank handRank)
	{
		return _matchesByRank.TryGetValue(handRank, out var matches) ? matches.Count : 0;
	}

	private void UpdatePokerHandButtonTexts()
	{
		foreach (var handRank in GetSelectableHandRanks())
		{
			if (!_rankButtons.TryGetValue(handRank, out var button))
			{
				continue;
			}

			button.Text = $"{GetHandDisplayName(handRank)} ({GetMatchCount(handRank)})";
		}
	}

	private void FlipAllCards()
	{
		if (_gridCells == null)
		{
			return;
		}

		var targetFaceUp = HasAnyFaceDownCard();
		_isUpdatingFaceUpStates = true;

		for (var row = 0; row < _gridCells.GetLength(0); row++)
		{
			for (var column = 0; column < _gridCells.GetLength(1); column++)
			{
				_gridCells[row, column]?.SetFaceUp(targetFaceUp, emitChanged: false);
			}
		}

		_isUpdatingFaceUpStates = false;
		RefreshPokerHandMatches();
	}

	private bool HasAnyFaceDownCard()
	{
		if (_gridCells == null)
		{
			return false;
		}

		for (var row = 0; row < _gridCells.GetLength(0); row++)
		{
			for (var column = 0; column < _gridCells.GetLength(1); column++)
			{
				if (_gridCells[row, column]?.IsFaceUp == false)
				{
					return true;
				}
			}
		}

		return false;
	}

	private static PokerHandRank[] GetSelectableHandRanks()
	{
		return new[]
		{
			PokerHandRank.RoyalFlush,
			PokerHandRank.StraightFlush,
			PokerHandRank.FourOfAKind,
			PokerHandRank.FullHouse,
			PokerHandRank.Flush,
			PokerHandRank.Straight,
			PokerHandRank.ThreeOfAKind,
			PokerHandRank.TwoPair,
			PokerHandRank.OnePair,
		};
	}

	private static string GetHandDisplayName(PokerHandRank handRank)
	{
		return handRank switch
		{
			PokerHandRank.OnePair => "\u4e00\u5bf9",
			PokerHandRank.TwoPair => "\u4e24\u5bf9",
			PokerHandRank.ThreeOfAKind => "\u4e09\u6761",
			PokerHandRank.Straight => "\u987a\u5b50",
			PokerHandRank.Flush => "\u540c\u82b1",
			PokerHandRank.FullHouse => "\u846b\u82a6",
			PokerHandRank.FourOfAKind => "\u56db\u6761",
			PokerHandRank.StraightFlush => "\u540c\u82b1\u987a",
			PokerHandRank.RoyalFlush => "\u7687\u5bb6\u540c\u82b1\u987a",
			_ => "\u6563\u724c",
		};
	}

	private static PokerHandRank EvaluatePokerHand(List<PlayingCard> hand)
	{
		var rankCounts = new Dictionary<int, int>();
		var isFlush = true;
		var firstSuit = hand[0].Suit;

		foreach (var card in hand)
		{
			if (!rankCounts.TryAdd(card.RankValue, 1))
			{
				rankCounts[card.RankValue]++;
			}

			if (card.Suit != firstSuit)
			{
				isFlush = false;
			}
		}

		var isStraight = IsStraight(rankCounts);
		if (isStraight && isFlush && rankCounts.ContainsKey(10) && rankCounts.ContainsKey(14))
		{
			return PokerHandRank.RoyalFlush;
		}

		if (isStraight && isFlush)
		{
			return PokerHandRank.StraightFlush;
		}

		var pairCount = 0;
		var hasThreeOfAKind = false;
		foreach (var count in rankCounts.Values)
		{
			if (count == 4)
			{
				return PokerHandRank.FourOfAKind;
			}

			if (count == 3)
			{
				hasThreeOfAKind = true;
			}
			else if (count == 2)
			{
				pairCount++;
			}
		}

		if (hasThreeOfAKind && pairCount == 1)
		{
			return PokerHandRank.FullHouse;
		}

		if (isFlush)
		{
			return PokerHandRank.Flush;
		}

		if (isStraight)
		{
			return PokerHandRank.Straight;
		}

		if (hasThreeOfAKind)
		{
			return PokerHandRank.ThreeOfAKind;
		}

		if (pairCount >= 2)
		{
			return PokerHandRank.TwoPair;
		}

		return pairCount == 1 ? PokerHandRank.OnePair : PokerHandRank.HighCard;
	}

	private static bool IsStraight(Dictionary<int, int> rankCounts)
	{
		if (rankCounts.Count != PokerHandCardCount)
		{
			return false;
		}

		if (rankCounts.ContainsKey(14) &&
			rankCounts.ContainsKey(2) &&
			rankCounts.ContainsKey(3) &&
			rankCounts.ContainsKey(4) &&
			rankCounts.ContainsKey(5))
		{
			return true;
		}

		var minRank = int.MaxValue;
		var maxRank = int.MinValue;
		foreach (var rank in rankCounts.Keys)
		{
			minRank = Mathf.Min(minRank, rank);
			maxRank = Mathf.Max(maxRank, rank);
		}

		return maxRank - minRank == PokerHandCardCount - 1;
	}

	private List<PlayingCard> CreateShuffledDeck()
	{
		var deck = new List<PlayingCard>();
		var ranks = new[]
		{
			(Rank: "A", Value: 14),
			(Rank: "2", Value: 2),
			(Rank: "3", Value: 3),
			(Rank: "4", Value: 4),
			(Rank: "5", Value: 5),
			(Rank: "6", Value: 6),
			(Rank: "7", Value: 7),
			(Rank: "8", Value: 8),
			(Rank: "9", Value: 9),
			(Rank: "10", Value: 10),
			(Rank: "J", Value: 11),
			(Rank: "Q", Value: 12),
			(Rank: "K", Value: 13),
		};

		foreach (CardSuit suit in System.Enum.GetValues<CardSuit>())
		{
			foreach (var rank in ranks)
			{
				deck.Add(new PlayingCard(suit, rank.Rank, rank.Value));
			}
		}

		var random = new RandomNumberGenerator();
		random.Randomize();

		for (var i = deck.Count - 1; i > 0; i--)
		{
			var swapIndex = random.RandiRange(0, i);
			(deck[i], deck[swapIndex]) = (deck[swapIndex], deck[i]);
		}

		return deck;
	}

	private Texture2D GetSuitTexture(CardSuit suit)
	{
		return suit switch
		{
			CardSuit.Hearts => HeartSuitTexture,
			CardSuit.Diamonds => DiamondSuitTexture,
			CardSuit.Clubs => ClubSuitTexture,
			CardSuit.Spades => SpadeSuitTexture,
			_ => null,
		};
	}

	private static Color GetSuitColor(CardSuit suit)
	{
		return suit is CardSuit.Hearts or CardSuit.Diamonds
			? new Color(0.82f, 0.08f, 0.06f)
			: new Color(0.08f, 0.08f, 0.08f);
	}
}
