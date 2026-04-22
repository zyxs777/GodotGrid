using Godot;
using System.Collections.Generic;

public partial class BattleRoot : Node2D
{
    private const int PokerHandCardCount = 5;
    private const string PokerHandUiName = "PokerHandUi";

    public enum GridDistanceMetric
    {
        Manhattan,
        Chebyshev
    }
    private enum BattlePhase
    {
        Placement,
        Battle
    }

    private enum CardSuit
    {
        Hearts,
        Diamonds,
        Clubs,
        Spades
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
        RoyalFlush
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

    [Export] private PackedScene _tileScene;
    [Export] private Camera2D _camera2D;

    [Export] private int _boardWidth = 8;
    [Export] private int _boardHeight = 8;

    [Export] private float _tileWidth = 128f;
    [Export] private float _tileHeight = 64f;
    [Export(PropertyHint.Range, "0.02,1,0.01")] private float _battleStepInterval = 0.08f;
    [Export] private GridDistanceMetric _attackDistanceMetric = GridDistanceMetric.Manhattan;
    [Export] private Texture2D _heartSuitTexture;
    [Export] private Texture2D _diamondSuitTexture;
    [Export] private Texture2D _clubSuitTexture;
    [Export] private Texture2D _spadeSuitTexture;
    [Export] private Vector2 _pokerUiMargin = new Vector2(20f, 20f);
    [Export] private float _pokerUiWidth = 150f;

    private Node2D _board;
    private Node2D _unitsRoot;

    private readonly Dictionary<Vector2I, Tile> _tilesByGridPos = new();
    private readonly Dictionary<Vector2I, Unit> _unitsByGridPos = new();

    private Tile _highlightedTile;
    private readonly List<Tile> _rangeHighlightedTiles = new();
    private Unit _selectedUnit;

    private Unit _draggingUnit;
    private Vector2I _dragStartGrid;
    private Vector2 _dragMouseOffset;

    private PlayingCard[,] _gridCards;
    private Tile[,] _gridTiles;
    private readonly Dictionary<PokerHandRank, List<PokerHandMatch>> _matchesByRank = new();
    private readonly List<Button> _pokerHandButtons = new();
    private readonly Dictionary<PokerHandRank, Button> _rankButtons = new();
    private PokerHandRank? _selectedHandRank;
    private bool _isUpdatingFaceUpStates;

    private BattlePhase _currentPhase = BattlePhase.Placement;
    private float _battleStepElapsed;
    private readonly HashSet<Unit> _pendingStepUnits = new();
    private readonly List<Unit> _allUnitsBuffer = new();
    private readonly List<Unit> _decisionOrderBuffer = new();
    private readonly List<MoveIntent> _moveIntentsBuffer = new();
    private int _decisionOrderCursor;

    public static GridDistanceMetric ActiveAttackDistanceMetric { get; private set; } = GridDistanceMetric.Manhattan;

    // Battle loop pipeline: strategy decides, resolver arbitrates, root executes.
    private readonly ChaseNearestEnemyStrategy _decisionStrategy = new();
    private readonly PriorityMoveResolver _moveResolver = new();

    public override void _Ready()
    {
        SetProcessUnhandledInput(true);
        SyncAttackDistanceMetric();

        _board = GetNode<Node2D>("Board");
        _unitsRoot = GetNode<Node2D>("Units");

        GenerateBoard();
        BindUnits();
        SnapExistingUnitsToGrid();
        CenterCamera();

        EnterPlacementPhase();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventKey keyEvent &&
            keyEvent.Pressed &&
            !keyEvent.Echo &&
            keyEvent.Keycode == Key.Space)
        {
            TogglePhase();
            return;
        }

        if (_currentPhase == BattlePhase.Placement && _draggingUnit != null)
        {
            if (@event is InputEventMouseMotion)
            {
                UpdateDraggingUnitPosition();
                UpdateDragHighlight();
                return;
            }

            if (@event is InputEventMouseButton dragMouseButton &&
                !dragMouseButton.Pressed &&
                dragMouseButton.ButtonIndex == MouseButton.Left)
            {
                TryDropDraggingUnit();
                return;
            }

            return;
        }

        if (@event is not InputEventMouseButton mouseButton)
        {
            return;
        }

        if (!mouseButton.Pressed || mouseButton.ButtonIndex != MouseButton.Left)
        {
            return;
        }

        ClearSelectedUnit();
        ClearHighlightedTile();
    }

    public override void _Process(double delta)
    {
        SyncAttackDistanceMetric();

        if (_currentPhase != BattlePhase.Battle)
        {
            return;
        }

        float deltaSeconds = (float)delta;
        TickCombatForAllUnits(deltaSeconds);

        bool stepFinished = UpdateAllUnitsMovement(deltaSeconds);

        if (stepFinished)
        {
            _battleStepElapsed -= deltaSeconds;
            if (_battleStepElapsed <= 0f)
            {
                _battleStepElapsed = _battleStepInterval;
                ExecuteBattleDecisionStep();
            }
        }

        ExecuteAttackPhase();
        CleanupDefeatedUnits();
    }

    private void SyncAttackDistanceMetric()
    {
        ActiveAttackDistanceMetric = _attackDistanceMetric;
    }

    public static int GetGridDistance(Vector2I a, Vector2I b)
    {
        int dx = Mathf.Abs(a.X - b.X);
        int dy = Mathf.Abs(a.Y - b.Y);

        return ActiveAttackDistanceMetric == GridDistanceMetric.Chebyshev
            ? Mathf.Max(dx, dy)
            : dx + dy;
    }
    private void GenerateBoard()
    {
        _tilesByGridPos.Clear();
        ClearChildren(_board);
        _gridCards = null;
        _gridTiles = new Tile[_boardHeight, _boardWidth];
        _matchesByRank.Clear();
        _selectedHandRank = null;

        for (int y = 0; y < _boardHeight; y++)
        {
            for (int x = 0; x < _boardWidth; x++)
            {
                Node node = _tileScene.Instantiate();
                Tile tile = node as Tile;

                if (tile == null)
                {
                    GD.PushError("Tile scene root is not Tile.");
                    return;
                }

                tile.Position = GridToWorld(x, y);
                tile.Init(new Vector2I(x, y));
                tile.CardFaceUpChanged += OnTileCardFaceUpChanged;
                _board.AddChild(tile);

                _tilesByGridPos[tile.GridPos] = tile;
                _gridTiles[y, x] = tile;
            }
        }

        DealCardsToBoard();
        BuildPokerHandUi();
    }

    private static void ClearChildren(Node node)
    {
        foreach (Node child in node.GetChildren())
        {
            node.RemoveChild(child);
            child.Free();
        }
    }

    private void DealCardsToBoard()
    {
        if (_gridTiles == null)
        {
            return;
        }

        int tileCount = _boardWidth * _boardHeight;
        List<PlayingCard> deck = CreateShuffledDeck();
        if (tileCount > deck.Count)
        {
            GD.PushWarning($"{Name} needs {tileCount} cards, but a poker deck has only {deck.Count} unique cards.");
            return;
        }

        _gridCards = new PlayingCard[_boardHeight, _boardWidth];
        _matchesByRank.Clear();
        _selectedHandRank = null;
        ClearCardHighlights();

        int cardIndex = 0;
        for (int y = 0; y < _boardHeight; y++)
        {
            for (int x = 0; x < _boardWidth; x++)
            {
                PlayingCard card = deck[cardIndex++];
                Tile tile = _gridTiles[y, x];
                _gridCards[y, x] = card;
                tile?.SetCard(GetSuitTexture(card.Suit), card.Rank, GetSuitColor(card.Suit));
            }
        }

        RefreshPokerHandMatches();
    }

    private void OnTileCardFaceUpChanged(Tile tile)
    {
        if (_isUpdatingFaceUpStates)
        {
            return;
        }

        RefreshPokerHandMatches();
    }

    private void CollectPokerHandMatches()
    {
        _matchesByRank.Clear();

        if (_gridCards == null || _gridTiles == null)
        {
            return;
        }

        var directions = new[]
        {
            (ColumnStep: 1, RowStep: 0),
            (ColumnStep: 0, RowStep: 1),
            (ColumnStep: 1, RowStep: 1),
            (ColumnStep: 1, RowStep: -1)
        };

        for (int row = 0; row < _boardHeight; row++)
        {
            for (int column = 0; column < _boardWidth; column++)
            {
                foreach (var direction in directions)
                {
                    int endRow = row + direction.RowStep * (PokerHandCardCount - 1);
                    int endColumn = column + direction.ColumnStep * (PokerHandCardCount - 1);
                    if (!IsInsideGrid(endRow, endColumn))
                    {
                        continue;
                    }

                    List<PlayingCard> hand = new(PokerHandCardCount);
                    Vector2I[] cells = new Vector2I[PokerHandCardCount];
                    bool allFaceUp = true;

                    for (int i = 0; i < PokerHandCardCount; i++)
                    {
                        int handRow = row + direction.RowStep * i;
                        int handColumn = column + direction.ColumnStep * i;

                        if (_gridTiles[handRow, handColumn]?.IsFaceUp != true)
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

                    PokerHandRank handRank = EvaluatePokerHand(hand);
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
            return;
        }

        ClearCardHighlights();
    }

    private bool IsInsideGrid(int row, int column)
    {
        return row >= 0 && row < _boardHeight && column >= 0 && column < _boardWidth;
    }

    private void AddPokerHandMatch(PokerHandMatch match)
    {
        if (!_matchesByRank.TryGetValue(match.Rank, out List<PokerHandMatch> matches))
        {
            matches = new List<PokerHandMatch>();
            _matchesByRank[match.Rank] = matches;
        }

        matches.Add(match);
    }

    private void BuildPokerHandUi()
    {
        CanvasLayer existingCanvas = GetNodeOrNull<CanvasLayer>(PokerHandUiName);
        if (existingCanvas != null)
        {
            RemoveChild(existingCanvas);
            existingCanvas.Free();
        }

        _pokerHandButtons.Clear();
        _rankButtons.Clear();

        CanvasLayer canvas = new()
        {
            Name = PokerHandUiName
        };
        AddChild(canvas);

        PanelContainer panel = new()
        {
            Name = "Panel",
            AnchorLeft = 1f,
            AnchorRight = 1f,
            AnchorTop = 0f,
            AnchorBottom = 0f,
            OffsetLeft = -_pokerUiWidth - _pokerUiMargin.X,
            OffsetRight = -_pokerUiMargin.X,
            OffsetTop = _pokerUiMargin.Y,
            OffsetBottom = _pokerUiMargin.Y + 460f
        };
        canvas.AddChild(panel);

        VBoxContainer buttonList = new()
        {
            Name = "HandButtons"
        };
        panel.AddChild(buttonList);

        Button clearButton = CreatePokerButton("\u53d6\u6d88\u9ad8\u4eae");
        clearButton.Pressed += ClearSelectedHand;
        buttonList.AddChild(clearButton);
        _pokerHandButtons.Add(clearButton);

        Button shuffleButton = CreatePokerButton("\u91cd\u65b0\u53d1\u724c");
        shuffleButton.Pressed += DealCardsToBoard;
        buttonList.AddChild(shuffleButton);
        _pokerHandButtons.Add(shuffleButton);

        Button flipAllButton = CreatePokerButton("\u5168\u90e8\u7ffb\u9762");
        flipAllButton.Pressed += FlipAllCards;
        buttonList.AddChild(flipAllButton);
        _pokerHandButtons.Add(flipAllButton);

        foreach (PokerHandRank handRank in GetSelectableHandRanks())
        {
            Button button = CreatePokerButton(string.Empty);
            button.Name = $"{handRank}Button";
            button.Pressed += () => ShowMatches(handRank);

            buttonList.AddChild(button);
            _pokerHandButtons.Add(button);
            _rankButtons[handRank] = button;
        }

        UpdatePokerHandButtonTexts();
    }

    private Button CreatePokerButton(string text)
    {
        return new Button
        {
            Text = text,
            CustomMinimumSize = new Vector2(_pokerUiWidth - 24f, 34f),
            FocusMode = Control.FocusModeEnum.None
        };
    }

    private void ShowMatches(PokerHandRank handRank)
    {
        _selectedHandRank = handRank;
        ClearCardHighlights();

        if (!_matchesByRank.TryGetValue(handRank, out List<PokerHandMatch> matches))
        {
            return;
        }

        foreach (PokerHandMatch match in matches)
        {
            foreach (Vector2I cellPosition in match.Cells)
            {
                if (!IsInsideGrid(cellPosition.Y, cellPosition.X))
                {
                    continue;
                }

                _gridTiles[cellPosition.Y, cellPosition.X]?.SetCardHighlighted(true);
            }
        }
    }

    private void ClearSelectedHand()
    {
        _selectedHandRank = null;
        ClearCardHighlights();
    }

    private void ClearCardHighlights()
    {
        if (_gridTiles == null)
        {
            return;
        }

        for (int row = 0; row < _gridTiles.GetLength(0); row++)
        {
            for (int column = 0; column < _gridTiles.GetLength(1); column++)
            {
                _gridTiles[row, column]?.SetCardHighlighted(false);
            }
        }
    }

    private int GetMatchCount(PokerHandRank handRank)
    {
        return _matchesByRank.TryGetValue(handRank, out List<PokerHandMatch> matches) ? matches.Count : 0;
    }

    private void UpdatePokerHandButtonTexts()
    {
        foreach (PokerHandRank handRank in GetSelectableHandRanks())
        {
            if (!_rankButtons.TryGetValue(handRank, out Button button))
            {
                continue;
            }

            button.Text = $"{GetHandDisplayName(handRank)} ({GetMatchCount(handRank)})";
        }
    }

    private void FlipAllCards()
    {
        if (_gridTiles == null)
        {
            return;
        }

        bool targetFaceUp = HasAnyFaceDownCard();
        _isUpdatingFaceUpStates = true;

        for (int row = 0; row < _gridTiles.GetLength(0); row++)
        {
            for (int column = 0; column < _gridTiles.GetLength(1); column++)
            {
                _gridTiles[row, column]?.SetFaceUp(targetFaceUp, emitChanged: false);
            }
        }

        _isUpdatingFaceUpStates = false;
        RefreshPokerHandMatches();
    }

    private bool HasAnyFaceDownCard()
    {
        if (_gridTiles == null)
        {
            return false;
        }

        for (int row = 0; row < _gridTiles.GetLength(0); row++)
        {
            for (int column = 0; column < _gridTiles.GetLength(1); column++)
            {
                if (_gridTiles[row, column]?.IsFaceUp == false)
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
            PokerHandRank.OnePair
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
            _ => "\u6563\u724c"
        };
    }

    private static PokerHandRank EvaluatePokerHand(List<PlayingCard> hand)
    {
        Dictionary<int, int> rankCounts = new();
        bool isFlush = true;
        CardSuit firstSuit = hand[0].Suit;

        foreach (PlayingCard card in hand)
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

        bool isStraight = IsStraight(rankCounts);
        if (isStraight && isFlush && rankCounts.ContainsKey(10) && rankCounts.ContainsKey(14))
        {
            return PokerHandRank.RoyalFlush;
        }

        if (isStraight && isFlush)
        {
            return PokerHandRank.StraightFlush;
        }

        int pairCount = 0;
        bool hasThreeOfAKind = false;
        foreach (int count in rankCounts.Values)
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

        int minRank = int.MaxValue;
        int maxRank = int.MinValue;
        foreach (int rank in rankCounts.Keys)
        {
            minRank = Mathf.Min(minRank, rank);
            maxRank = Mathf.Max(maxRank, rank);
        }

        return maxRank - minRank == PokerHandCardCount - 1;
    }

    private List<PlayingCard> CreateShuffledDeck()
    {
        List<PlayingCard> deck = new();
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
            (Rank: "K", Value: 13)
        };

        foreach (CardSuit suit in System.Enum.GetValues<CardSuit>())
        {
            foreach (var rank in ranks)
            {
                deck.Add(new PlayingCard(suit, rank.Rank, rank.Value));
            }
        }

        RandomNumberGenerator random = new();
        random.Randomize();

        for (int i = deck.Count - 1; i > 0; i--)
        {
            int swapIndex = random.RandiRange(0, i);
            (deck[i], deck[swapIndex]) = (deck[swapIndex], deck[i]);
        }

        return deck;
    }

    private Texture2D GetSuitTexture(CardSuit suit)
    {
        return suit switch
        {
            CardSuit.Hearts => _heartSuitTexture,
            CardSuit.Diamonds => _diamondSuitTexture,
            CardSuit.Clubs => _clubSuitTexture,
            CardSuit.Spades => _spadeSuitTexture,
            _ => null
        };
    }

    private static Color GetSuitColor(CardSuit suit)
    {
        return suit is CardSuit.Hearts or CardSuit.Diamonds
            ? new Color(0.82f, 0.08f, 0.06f)
            : new Color(0.08f, 0.08f, 0.08f);
    }

    private void BindUnits()
    {
        foreach (Node child in _unitsRoot.GetChildren())
        {
            if (child is Unit unit)
            {
                unit.Clicked += OnUnitClicked;
            }
        }
    }

    private void SnapExistingUnitsToGrid()
    {
        _unitsByGridPos.Clear();

        foreach (Node child in _unitsRoot.GetChildren())
        {
            if (child is not Unit unit)
            {
                continue;
            }

            Vector2 boardLocalPos = _board.ToLocal(unit.GlobalPosition);

            if (!TryWorldToGrid(boardLocalPos, out Vector2I gridPos))
            {
                GD.PushWarning($"{unit.Name} is outside board bounds and was not snapped.");
                continue;
            }

            if (_unitsByGridPos.ContainsKey(gridPos))
            {
                GD.PushWarning($"{unit.Name} snapped to occupied grid {gridPos}, skipped registering.");
                continue;
            }

            RegisterUnitAtGrid(unit, gridPos);
        }
    }

    private void OnUnitClicked(Unit unit)
    {
        if (_draggingUnit != null)
        {
            return;
        }

        if (_selectedUnit == unit)
        {
            ClearSelectedUnit();
            return;
        }

        if (_selectedUnit != null)
        {
            ClearSelectedUnit();
        }

        _selectedUnit = unit;
        _selectedUnit.SetSelected(true);
        ShowSelectedUnitAttackRange(_selectedUnit);

        if (_currentPhase != BattlePhase.Placement)
        {
            return;
        }

        /*if (unit.Team != UnitTeam.Player)
        {
            return;
        }*/

        _draggingUnit = unit;
        _dragStartGrid = unit.GridPos;
        _dragMouseOffset = unit.GlobalPosition - GetGlobalMousePosition();

        UpdateDragHighlight();
    }

    private void UpdateDraggingUnitPosition()
    {
        if (_draggingUnit == null)
        {
            return;
        }

        _draggingUnit.GlobalPosition = GetGlobalMousePosition() + _dragMouseOffset;
        UpdateDragHighlight();
    }

    private void UpdateDragHighlight()
    {
        if (_draggingUnit == null)
        {
            ClearHighlightedTile();
            if (_selectedUnit != null)
            {
                ShowSelectedUnitAttackRange(_selectedUnit);
            }
            return;
        }

        Vector2 boardLocalPos = _board.ToLocal(_draggingUnit.GlobalPosition);

        if (TryWorldToGrid(boardLocalPos, out Vector2I grid))
        {
            HighlightTile(grid);
            ShowSelectedUnitAttackRange(_draggingUnit, grid);
        }
        else
        {
            ClearHighlightedTile();
            ClearSelectedUnitAttackRange();
        }
    }

    private void TryDropDraggingUnit()
    {
        if (_draggingUnit == null)
        {
            return;
        }

        Vector2 boardLocalPos = _board.ToLocal(_draggingUnit.GlobalPosition);

        if (!TryWorldToGrid(boardLocalPos, out Vector2I targetGrid))
        {
            RegisterUnitAtGrid(_draggingUnit, _dragStartGrid);
            FinishDragging();
            return;
        }

        if (targetGrid == _dragStartGrid)
        {
            RegisterUnitAtGrid(_draggingUnit, _dragStartGrid);
            FinishDragging();
            return;
        }

        if (_unitsByGridPos.TryGetValue(targetGrid, out Unit occupant) && occupant != _draggingUnit)
        {
            SwapUnits(_draggingUnit, occupant);
            FinishDragging();
            return;
        }

        RegisterUnitAtGrid(_draggingUnit, targetGrid);
        FinishDragging();
    }

    private void SwapUnits(Unit a, Unit b)
    {
        Vector2I aGrid = a.GridPos;
        Vector2I bGrid = b.GridPos;

        _unitsByGridPos.Remove(aGrid);
        _unitsByGridPos.Remove(bGrid);

        RegisterUnitAtGrid(a, bGrid);
        RegisterUnitAtGrid(b, aGrid);
    }

    private void RegisterUnitAtGrid(Unit unit, Vector2I gridPos)
    {
        if (_unitsByGridPos.TryGetValue(unit.GridPos, out Unit current) && current == unit)
        {
            _unitsByGridPos.Remove(unit.GridPos);
        }

        unit.SetGridPos(gridPos);
        unit.GlobalPosition = _board.ToGlobal(GridToWorld(gridPos.X, gridPos.Y));
        _unitsByGridPos[gridPos] = unit;
    }

    private void FinishDragging()
    {
        ClearHighlightedTile();
        _draggingUnit = null;

        if (_selectedUnit != null)
        {
            ShowSelectedUnitAttackRange(_selectedUnit);
        }
    }

    private void HighlightTile(Vector2I gridPos)
    {
        if (!_tilesByGridPos.TryGetValue(gridPos, out Tile tile))
        {
            ClearHighlightedTile();
            return;
        }

        if (_highlightedTile == tile)
        {
            return;
        }

        if (_highlightedTile != null)
        {
            _highlightedTile.SetCursorHighlighted(false);
        }

        _highlightedTile = tile;
        _highlightedTile.SetCursorHighlighted(true);
    }

    private void ClearHighlightedTile()
    {
        if (_highlightedTile == null)
        {
            return;
        }

        _highlightedTile.SetCursorHighlighted(false);
        _highlightedTile = null;
    }

    private void ClearSelectedUnit()
    {
        if (_selectedUnit == null)
        {
            return;
        }

        _selectedUnit.SetSelected(false);
        ClearSelectedUnitAttackRange();
        _selectedUnit = null;
    }

    private void ShowSelectedUnitAttackRange(Unit unit)
    {
        if (unit == null)
        {
            ClearSelectedUnitAttackRange();
            return;
        }

        ShowSelectedUnitAttackRange(unit, unit.GridPos);
    }

    private void ShowSelectedUnitAttackRange(Unit unit, Vector2I originGrid)
    {
        ClearSelectedUnitAttackRange();

        if (unit == null)
        {
            return;
        }

        int range = unit.MaxAttackRange;
        if (range <= 0)
        {
            return;
        }

        for (int y = 0; y < _boardHeight; y++)
        {
            for (int x = 0; x < _boardWidth; x++)
            {
                Vector2I gridPos = new(x, y);
                int distance = GetGridDistance(gridPos, originGrid);
                if (distance == 0 || distance > range)
                {
                    continue;
                }

                if (!_tilesByGridPos.TryGetValue(gridPos, out Tile tile))
                {
                    continue;
                }

                tile.SetRangeHighlighted(true);
                _rangeHighlightedTiles.Add(tile);
            }
        }
    }

    private void ClearSelectedUnitAttackRange()
    {
        foreach (Tile tile in _rangeHighlightedTiles)
        {
            tile?.SetRangeHighlighted(false);
        }

        _rangeHighlightedTiles.Clear();
    }

    private List<Unit> GetAllUnits()
    {
        _allUnitsBuffer.Clear();

        foreach (Node child in _unitsRoot.GetChildren())
        {
            if (child is Unit unit && unit.IsAlive)
            {
                _allUnitsBuffer.Add(unit);
            }
        }

        return _allUnitsBuffer;
    }

    private void ExecuteBattleDecisionStep()
    {
        List<Unit> allUnits = GetAllUnits();
        if (allUnits.Count == 0)
        {
            return;
        }

        List<Unit> decisionOrder = BuildDecisionOrder(allUnits);
        BattleSnapshot snapshot = new(_boardWidth, _boardHeight, decisionOrder);
        _moveIntentsBuffer.Clear();

        foreach (Unit unit in decisionOrder)
        {
            UnitDecision decision = _decisionStrategy.Decide(unit, snapshot);
            Unit preferredTarget = decision.AttackTarget ?? decision.PrimaryTarget;
            unit.SetCurrentTarget(preferredTarget);

            if (unit.IsAttacking)
            {
                if (preferredTarget != null)
                {
                    unit.FaceTarget(preferredTarget);
                }

                continue;
            }

            if (preferredTarget != null && decision.MoveDestination is not Vector2I)
            {
                unit.FaceTarget(preferredTarget);
            }

            if (decision.MoveDestination is not Vector2I moveDestination)
            {
                continue;
            }

            _moveIntentsBuffer.Add(new MoveIntent(
                unit,
                unit.GridPos,
                moveDestination,
                decision.MovePriority));
            snapshot.ReserveMove(unit, moveDestination);
        }

        if (decisionOrder.Count > 0)
        {
            _decisionOrderCursor = (_decisionOrderCursor + 1) % decisionOrder.Count;
        }

        Dictionary<Unit, Vector2I> resolvedMoves = _moveResolver.Resolve(_moveIntentsBuffer, snapshot);
        _pendingStepUnits.Clear();

        foreach (KeyValuePair<Unit, Vector2I> move in resolvedMoves)
        {
            Unit unit = move.Key;
            Vector2I toGrid = move.Value;
            Vector2 toWorld = _board.ToGlobal(GridToWorld(toGrid.X, toGrid.Y));
            unit.BeginStepMove(toGrid, toWorld);
            _pendingStepUnits.Add(unit);
        }
    }

    private List<Unit> BuildDecisionOrder(List<Unit> allUnits)
    {
        _decisionOrderBuffer.Clear();
        _decisionOrderBuffer.AddRange(allUnits);
        _decisionOrderBuffer.Sort(static (a, b) => a.GetInstanceId().CompareTo(b.GetInstanceId()));

        if (_decisionOrderBuffer.Count <= 1)
        {
            _decisionOrderCursor = 0;
            return _decisionOrderBuffer;
        }

        if (_decisionOrderCursor < 0 || _decisionOrderCursor >= _decisionOrderBuffer.Count)
        {
            _decisionOrderCursor = 0;
        }

        if (_decisionOrderCursor == 0)
        {
            return _decisionOrderBuffer;
        }

        List<Unit> rotated = new(_decisionOrderBuffer.Count);
        for (int i = 0; i < _decisionOrderBuffer.Count; i++)
        {
            int index = (i + _decisionOrderCursor) % _decisionOrderBuffer.Count;
            rotated.Add(_decisionOrderBuffer[index]);
        }

        _decisionOrderBuffer.Clear();
        _decisionOrderBuffer.AddRange(rotated);
        return _decisionOrderBuffer;
    }

    private bool UpdateAllUnitsMovement(float delta)
    {
        if (_pendingStepUnits.Count == 0)
        {
            return true;
        }

        List<Unit> completedUnits = new();

        foreach (Unit unit in _pendingStepUnits)
        {
            if (unit == null || !unit.IsAlive)
            {
                completedUnits.Add(unit);
                continue;
            }

            if (!unit.TryAdvanceAlongPath(delta, out Vector2I fromGrid, out Vector2I toGrid))
            {
                continue;
            }

            if (_unitsByGridPos.TryGetValue(toGrid, out Unit toOccupant) &&
                toOccupant != unit &&
                toOccupant.IsAlive)
            {
                Vector2 fromWorld = _board.ToGlobal(GridToWorld(fromGrid.X, fromGrid.Y));
                unit.AbortCurrentStep(fromWorld);
                completedUnits.Add(unit);
                continue;
            }

            if (_unitsByGridPos.TryGetValue(fromGrid, out Unit fromOccupant) && fromOccupant == unit)
            {
                _unitsByGridPos.Remove(fromGrid);
            }

            _unitsByGridPos[toGrid] = unit;
            unit.CommitReachedWaypoint(toGrid);
            completedUnits.Add(unit);
        }

        foreach (Unit unit in completedUnits)
        {
            _pendingStepUnits.Remove(unit);
        }

        return _pendingStepUnits.Count == 0;
    }

    private void TickCombatForAllUnits(float delta)
    {
        foreach (Unit unit in GetAllUnits())
        {
            unit.TickCombat(delta);
        }
    }

    private void ExecuteAttackPhase()
    {
        foreach (Unit unit in GetAllUnits())
        {
            if (unit.IsMoving || unit.IsAttacking)
            {
                continue;
            }

            Unit target = unit.CurrentTarget;
            if (target == null || !target.IsAlive)
            {
                unit.ClearTarget();
                continue;
            }

            unit.FaceTarget(target);
            unit.StartAttack(target);
        }
    }

    private void CleanupDefeatedUnits()
    {
        List<Unit> defeated = new();

        foreach (Node child in _unitsRoot.GetChildren())
        {
            if (child is Unit unit && !unit.IsAlive)
            {
                defeated.Add(unit);
            }
        }

        if (defeated.Count == 0)
        {
            return;
        }

        HashSet<Unit> defeatedSet = new(defeated);

        foreach (Unit aliveUnit in GetAllUnits())
        {
            if (defeatedSet.Contains(aliveUnit.CurrentTarget))
            {
                aliveUnit.ClearTarget();
            }
        }

        foreach (Unit deadUnit in defeated)
        {
            _pendingStepUnits.Remove(deadUnit);

            if (_selectedUnit == deadUnit)
            {
                ClearSelectedUnitAttackRange();
                _selectedUnit = null;
            }

            if (_draggingUnit == deadUnit)
            {
                _draggingUnit = null;
                ClearHighlightedTile();
            }

            if (_unitsByGridPos.TryGetValue(deadUnit.GridPos, out Unit occupant) && occupant == deadUnit)
            {
                _unitsByGridPos.Remove(deadUnit.GridPos);
            }

            deadUnit.QueueFree();
        }
    }

    private void EnterPlacementPhase()
    {
        _currentPhase = BattlePhase.Placement;
        _pendingStepUnits.Clear();
        _decisionOrderCursor = 0;

        foreach (Unit unit in GetAllUnits())
        {
            unit.ResetMoveState();
            unit.ClearTarget();
        }

        GD.Print("Enter Placement Phase");
    }

    private void EnterBattlePhase()
    {
        _currentPhase = BattlePhase.Battle;
        _decisionOrderCursor = 0;

        if (_draggingUnit != null)
        {
            RegisterUnitAtGrid(_draggingUnit, _dragStartGrid);
            _draggingUnit = null;
        }

        ClearHighlightedTile();
        ClearSelectedUnit();

        _pendingStepUnits.Clear();
        _battleStepElapsed = 0f;
        ExecuteBattleDecisionStep();

        GD.Print("Enter Battle Phase");
    }

    private void TogglePhase()
    {
        if (_currentPhase == BattlePhase.Placement)
        {
            EnterBattlePhase();
        }
        else
        {
            EnterPlacementPhase();
        }
    }

    private void CenterCamera()
    {
        if (_camera2D == null)
        {
            GD.PushWarning("Camera2D is not assigned.");
            return;
        }

        _camera2D.Position = GetBoardCenter();
    }

    private Vector2 GridToWorld(int x, int y)
    {
        float worldX = (x - y) * (_tileWidth * 0.5f);
        float worldY = (x + y) * (_tileHeight * 0.5f);
        return new Vector2(worldX, worldY);
    }

    private bool TryWorldToGrid(Vector2 worldPos, out Vector2I grid)
    {
        float halfW = _tileWidth * 0.5f;
        float halfH = _tileHeight * 0.5f;

        float gx = (worldPos.X / halfW + worldPos.Y / halfH) * 0.5f;
        float gy = (worldPos.Y / halfH - worldPos.X / halfW) * 0.5f;

        int x = Mathf.RoundToInt(gx);
        int y = Mathf.RoundToInt(gy);

        if (x >= 0 && x < _boardWidth && y >= 0 && y < _boardHeight)
        {
            grid = new Vector2I(x, y);
            return true;
        }

        grid = default;
        return false;
    }

    private Vector2 GetBoardCenter()
    {
        Vector2 topRight = GridToWorld(_boardWidth - 1, 0);
        Vector2 bottomLeft = GridToWorld(0, _boardHeight - 1);
        return (topRight + bottomLeft) * 0.5f;
    }
}

