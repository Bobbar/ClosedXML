#nullable disable

using ClosedXML.Excel.Caching;
using ClosedXML.Excel.CalcEngine;
using ClosedXML.Excel.Drawings;
using ClosedXML.Excel.Ranges.Index;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ClosedXML.Excel.InsertData;
using static ClosedXML.Excel.XLProtectionAlgorithm;

namespace ClosedXML.Excel
{
    internal class XLWorksheet : XLRangeBase, IXLWorksheet
    {
        #region Fields

        private readonly Dictionary<Int32, Int32> _columnOutlineCount = new Dictionary<Int32, Int32>();
        private readonly Dictionary<Int32, Int32> _rowOutlineCount = new Dictionary<Int32, Int32>();
        private readonly XLRangeFactory _rangeFactory;
        private readonly XLRangeRepository _rangeRepository;
        private readonly List<IXLRangeIndex> _rangeIndices;
        internal Int32 ZOrder = 1;
        private String _name;
        internal Int32 _position;

        private Double _rowHeight;
        private Boolean _tabActive;
        private XLSheetProtection _protection;

        /// <summary>
        /// Fake address to be used everywhere the invalid address is needed.
        /// </summary>
        internal readonly XLAddress InvalidAddress;

        #endregion Fields

        #region Constructor

        public XLWorksheet(String sheetName, XLWorkbook workbook, UInt32 sheetId)
            : base(
                new XLRangeAddress(
                    new XLAddress(null, XLHelper.MinRowNumber, XLHelper.MinColumnNumber, false, false),
                    new XLAddress(null, XLHelper.MaxRowNumber, XLHelper.MaxColumnNumber, false, false)),
                (workbook.Style as XLStyle).Value)
        {
            Workbook = workbook;
            SheetId = sheetId;
            InvalidAddress = new XLAddress(this, 0, 0, false, false);

            var firstAddress = new XLAddress(this, RangeAddress.FirstAddress.RowNumber, RangeAddress.FirstAddress.ColumnNumber,
                                                   RangeAddress.FirstAddress.FixedRow, RangeAddress.FirstAddress.FixedColumn);
            var lastAddress = new XLAddress(this, RangeAddress.LastAddress.RowNumber, RangeAddress.LastAddress.ColumnNumber,
                                                  RangeAddress.LastAddress.FixedRow, RangeAddress.LastAddress.FixedColumn);
            RangeAddress = new XLRangeAddress(firstAddress, lastAddress);
            _rangeFactory = new XLRangeFactory(this);
            _rangeRepository = new XLRangeRepository(workbook, _rangeFactory.Create);
            _rangeIndices = new List<IXLRangeIndex>();

            Pictures = new XLPictures(this);
            NamedRanges = new XLNamedRanges(this);
            SheetView = new XLSheetView(this);
            Tables = new XLTables();
            Hyperlinks = new XLHyperlinks();
            DataValidations = new XLDataValidations(this);
            PivotTables = new XLPivotTables(this);
            Protection = new XLSheetProtection(DefaultProtectionAlgorithm);
            AutoFilter = new XLAutoFilter();
            ConditionalFormats = new XLConditionalFormats();
            SparklineGroupsInternal = new XLSparklineGroups(this);
            Internals = new XLWorksheetInternals(new XLCellsCollection(this), new XLColumnsCollection(),
                                                 new XLRowsCollection(), new XLRanges());
            PageSetup = new XLPageSetup((XLPageSetup)workbook.PageOptions, this);
            Outline = new XLOutline(workbook.Outline);
            _columnWidth = workbook.ColumnWidth;
            _rowHeight = workbook.RowHeight;
            RowHeightChanged = Math.Abs(workbook.RowHeight - XLWorkbook.DefaultRowHeight) > XLHelper.Epsilon;
            Name = sheetName;
            Charts = new XLCharts();
            ShowFormulas = workbook.ShowFormulas;
            ShowGridLines = workbook.ShowGridLines;
            ShowOutlineSymbols = workbook.ShowOutlineSymbols;
            ShowRowColHeaders = workbook.ShowRowColHeaders;
            ShowRuler = workbook.ShowRuler;
            ShowWhiteSpace = workbook.ShowWhiteSpace;
            ShowZeros = workbook.ShowZeros;
            RightToLeft = workbook.RightToLeft;
            TabColor = XLColor.NoColor;
            SelectedRanges = new XLRanges();

            Author = workbook.Author;
        }

        #endregion Constructor

        public override XLRangeType RangeType
        {
            get { return XLRangeType.Worksheet; }
        }

        /// <summary>
        /// Reference to a VML that contains notes, forms controls and so on. All such things are generally unified into
        /// a single legacy VML file, set during load/save.
        /// </summary>
        public string LegacyDrawingId;

        private Double _columnWidth;

        public XLWorksheetInternals Internals { get; private set; }

        internal XLSparklineGroups SparklineGroupsInternal { get; }

        public XLRangeFactory RangeFactory
        {
            get { return _rangeFactory; }
        }

        public override IEnumerable<IXLStyle> Styles
        {
            get
            {
                yield return GetStyle();
                foreach (XLCell c in Internals.CellsCollection.GetCells())
                    yield return c.Style;
            }
        }

        protected override IEnumerable<XLStylizedBase> Children
        {
            get
            {
                var columnsUsed = Internals.ColumnsCollection.Keys
                    .Union(Internals.CellsCollection.ColumnsUsedKeys)
                    .Distinct()
                    .OrderBy(c => c)
                    .ToList();
                foreach (var col in columnsUsed)
                    yield return Column(col);

                var rowsUsed = Internals.RowsCollection.Keys
                    .Union(Internals.CellsCollection.RowsUsedKeys)
                    .Distinct()
                    .OrderBy(r => r)
                    .ToList();
                foreach (var row in rowsUsed)
                    yield return Row(row);
            }
        }

        internal Boolean RowHeightChanged { get; set; }

        internal Boolean ColumnWidthChanged { get; set; }

        /// <summary>
        /// <para>
        /// The id of a sheet that is unique across the workbook, kept across load/save.
        /// The ids of sheets are not reused. That is important for referencing the sheet
        /// range/point through sheetId. If sheetIds were reused, references would refer
        /// to the wrong sheet after the original sheetId was reused. Excel also doesn't
        /// reuse sheetIds.
        /// </para>
        /// <para>
        /// Referencing sheet through non-reused sheetIds means that reference can survive
        /// sheet renaming without any changes. Always &gt; 0 (Excel will crash on 0).
        /// </para>
        /// </summary>
        internal UInt32 SheetId { get; set; }

        /// <summary>
        /// A cached <c>r:id</c> of the sheet from the file. If the sheet is a new one (not
        /// yet saved), the value is null until workbook is saved. Use <see cref="SheetId"/>
        /// instead is possible. Mostly for removing deleted sheet parts during save.
        /// </summary>
        internal String RelId { get; set; }

        public XLDataValidations DataValidations { get; private set; }

        public IXLCharts Charts { get; private set; }

        public XLSheetProtection Protection
        {
            get => _protection;
            set
            {
                _protection = value.Clone().CastTo<XLSheetProtection>();
            }
        }

        public XLAutoFilter AutoFilter { get; private set; }

        public bool IsDeleted { get; private set; }

        #region IXLWorksheet Members

        public XLWorkbook Workbook { get; private set; }

        public Double ColumnWidth
        {
            get { return _columnWidth; }
            set
            {
                ColumnWidthChanged = true;
                _columnWidth = value;
            }
        }

        public Double RowHeight
        {
            get { return _rowHeight; }
            set
            {
                RowHeightChanged = true;
                _rowHeight = value;
            }
        }

        public String Name
        {
            get { return _name; }
            set
            {
                if (_name == value) return;

                XLHelper.ValidateSheetName(value);

                Workbook.WorksheetsInternal.Rename(_name, value);
                _name = value;
            }
        }

        public Int32 Position
        {
            get { return _position; }
            set
            {
                if (value > Workbook.WorksheetsInternal.Count + Workbook.UnsupportedSheets.Count + 1)
                    throw new ArgumentOutOfRangeException(nameof(value), "Index must be equal or less than the number of worksheets + 1.");

                if (value < _position)
                {
                    Workbook.WorksheetsInternal
                        .Where<XLWorksheet>(w => w.Position >= value && w.Position < _position)
                        .ForEach(w => w._position += 1);
                }

                if (value > _position)
                {
                    Workbook.WorksheetsInternal
                        .Where<XLWorksheet>(w => w.Position <= value && w.Position > _position)
                        .ForEach(w => (w)._position -= 1);
                }

                _position = value;
            }
        }

        public IXLPageSetup PageSetup { get; private set; }

        public IXLOutline Outline { get; private set; }

        IXLRow IXLWorksheet.FirstRowUsed()
        {
            return FirstRowUsed();
        }


        IXLRow IXLWorksheet.FirstRowUsed(XLCellsUsedOptions options)
        {
            return FirstRowUsed(options);
        }

        IXLRow IXLWorksheet.LastRowUsed()
        {
            return LastRowUsed();
        }

        IXLRow IXLWorksheet.LastRowUsed(XLCellsUsedOptions options)
        {
            return LastRowUsed(options);
        }

        IXLColumn IXLWorksheet.LastColumn()
        {
            return LastColumn();
        }

        IXLColumn IXLWorksheet.FirstColumn()
        {
            return FirstColumn();
        }

        IXLRow IXLWorksheet.FirstRow()
        {
            return FirstRow();
        }

        IXLRow IXLWorksheet.LastRow()
        {
            return LastRow();
        }

        IXLColumn IXLWorksheet.FirstColumnUsed()
        {
            return FirstColumnUsed();
        }

        IXLColumn IXLWorksheet.FirstColumnUsed(XLCellsUsedOptions options)
        {
            return FirstColumnUsed(options);
        }

        IXLColumn IXLWorksheet.LastColumnUsed()
        {
            return LastColumnUsed();
        }

        IXLColumn IXLWorksheet.LastColumnUsed(XLCellsUsedOptions options)
        {
            return LastColumnUsed(options);
        }

        public IXLColumns Columns()
        {
            var columnMap = new HashSet<Int32>();

            columnMap.UnionWith(Internals.CellsCollection.ColumnsUsedKeys);
            columnMap.UnionWith(Internals.ColumnsCollection.Keys);

            var retVal = new XLColumns(this, StyleValue, columnMap.Select(Column));
            return retVal;
        }

        public IXLColumns Columns(String columns)
        {
            var retVal = new XLColumns(null, StyleValue);
            var columnPairs = columns.Split(',');
            foreach (string tPair in columnPairs.Select(pair => pair.Trim()))
            {
                String firstColumn;
                String lastColumn;
                if (tPair.Contains(':') || tPair.Contains('-'))
                {
                    var columnRange = XLHelper.SplitRange(tPair);
                    firstColumn = columnRange[0];
                    lastColumn = columnRange[1];
                }
                else
                {
                    firstColumn = tPair;
                    lastColumn = tPair;
                }

                if (Int32.TryParse(firstColumn, out int tmp))
                {
                    foreach (IXLColumn col in Columns(Int32.Parse(firstColumn), Int32.Parse(lastColumn)))
                        retVal.Add((XLColumn)col);
                }
                else
                {
                    foreach (IXLColumn col in Columns(firstColumn, lastColumn))
                        retVal.Add((XLColumn)col);
                }
            }
            return retVal;
        }

        public IXLColumns Columns(String firstColumn, String lastColumn)
        {
            return Columns(XLHelper.GetColumnNumberFromLetter(firstColumn),
                           XLHelper.GetColumnNumberFromLetter(lastColumn));
        }

        public IXLColumns Columns(Int32 firstColumn, Int32 lastColumn)
        {
            var retVal = new XLColumns(null, StyleValue);

            for (int co = firstColumn; co <= lastColumn; co++)
                retVal.Add(Column(co));
            return retVal;
        }

        public IXLRows Rows()
        {
            var rowMap = new HashSet<Int32>();

            rowMap.UnionWith(Internals.CellsCollection.RowsUsedKeys);
            rowMap.UnionWith(Internals.RowsCollection.Keys);

            var retVal = new XLRows(this, StyleValue, rowMap.Select(Row));
            return retVal;
        }

        public IXLRows Rows(String rows)
        {
            var retVal = new XLRows(null, StyleValue);
            var rowPairs = rows.Split(',');
            foreach (string tPair in rowPairs.Select(pair => pair.Trim()))
            {
                String firstRow;
                String lastRow;
                if (tPair.Contains(':') || tPair.Contains('-'))
                {
                    var rowRange = XLHelper.SplitRange(tPair);
                    firstRow = rowRange[0];
                    lastRow = rowRange[1];
                }
                else
                {
                    firstRow = tPair;
                    lastRow = tPair;
                }

                Rows(Int32.Parse(firstRow), Int32.Parse(lastRow))
                    .ForEach(row => retVal.Add((XLRow)row));
            }
            return retVal;
        }

        public IXLRows Rows(Int32 firstRow, Int32 lastRow)
        {
            var retVal = new XLRows(null, StyleValue);

            for (int ro = firstRow; ro <= lastRow; ro++)
                retVal.Add(Row(ro));
            return retVal;
        }

        IXLRow IXLWorksheet.Row(Int32 row)
        {
            return Row(row);
        }

        IXLColumn IXLWorksheet.Column(Int32 column)
        {
            return Column(column);
        }

        IXLColumn IXLWorksheet.Column(String column)
        {
            return Column(column);
        }

        IXLCell IXLWorksheet.Cell(int row, int column)
        {
            return Cell(row, column);
        }

        IXLCell IXLWorksheet.Cell(string cellAddressInRange)
        {
            return Cell(cellAddressInRange);
        }

        IXLCell IXLWorksheet.Cell(int row, string column)
        {
            return Cell(row, column);
        }

        IXLCell IXLWorksheet.Cell(IXLAddress cellAddressInRange)
        {
            return Cell(cellAddressInRange);
        }

        IXLRange IXLWorksheet.Range(IXLRangeAddress rangeAddress)
        {
            return Range(rangeAddress);
        }

        IXLRange IXLWorksheet.Range(string rangeAddress)
        {
            return Range(rangeAddress);
        }

        IXLRange IXLWorksheet.Range(IXLCell firstCell, IXLCell lastCell)
        {
            return Range(firstCell, lastCell);
        }

        IXLRange IXLWorksheet.Range(string firstCellAddress, string lastCellAddress)
        {
            return Range(firstCellAddress, lastCellAddress);
        }

        IXLRange IXLWorksheet.Range(IXLAddress firstCellAddress, IXLAddress lastCellAddress)
        {
            return Range(firstCellAddress, lastCellAddress);
        }

        IXLRange IXLWorksheet.Range(int firstCellRow, int firstCellColumn, int lastCellRow, int lastCellColumn)
        {
            return Range(firstCellRow, firstCellColumn, lastCellRow, lastCellColumn);
        }

        IXLRanges IXLWorksheet.Ranges(String ranges) => Ranges(ranges);

        public IXLWorksheet CollapseRows()
        {
            Enumerable.Range(1, 8).ForEach(i => CollapseRows(i));
            return this;
        }

        public IXLWorksheet CollapseColumns()
        {
            Enumerable.Range(1, 8).ForEach(i => CollapseColumns(i));
            return this;
        }

        public IXLWorksheet ExpandRows()
        {
            Enumerable.Range(1, 8).ForEach(i => ExpandRows(i));
            return this;
        }

        public IXLWorksheet ExpandColumns()
        {
            Enumerable.Range(1, 8).ForEach(i => ExpandColumns(i));
            return this;
        }

        public IXLWorksheet CollapseRows(Int32 outlineLevel)
        {
            if (outlineLevel < 1 || outlineLevel > 8)
                throw new ArgumentOutOfRangeException("outlineLevel", "Outline level must be between 1 and 8.");

            Internals.RowsCollection.Values.Where(r => r.OutlineLevel == outlineLevel).ForEach(r => r.Collapse());
            return this;
        }

        public IXLWorksheet CollapseColumns(Int32 outlineLevel)
        {
            if (outlineLevel < 1 || outlineLevel > 8)
                throw new ArgumentOutOfRangeException("outlineLevel", "Outline level must be between 1 and 8.");

            Internals.ColumnsCollection.Values.Where(c => c.OutlineLevel == outlineLevel).ForEach(c => c.Collapse());
            return this;
        }

        public IXLWorksheet ExpandRows(Int32 outlineLevel)
        {
            if (outlineLevel < 1 || outlineLevel > 8)
                throw new ArgumentOutOfRangeException("outlineLevel", "Outline level must be between 1 and 8.");

            Internals.RowsCollection.Values.Where(r => r.OutlineLevel == outlineLevel).ForEach(r => r.Expand());
            return this;
        }

        public IXLWorksheet ExpandColumns(Int32 outlineLevel)
        {
            if (outlineLevel < 1 || outlineLevel > 8)
                throw new ArgumentOutOfRangeException("outlineLevel", "Outline level must be between 1 and 8.");

            Internals.ColumnsCollection.Values.Where(c => c.OutlineLevel == outlineLevel).ForEach(c => c.Expand());
            return this;
        }

        public void Delete()
        {
            IsDeleted = true;
            (Workbook.NamedRanges as XLNamedRanges).OnWorksheetDeleted(Name);
            Workbook.NotifyWorksheetDeleting(this);
            Workbook.WorksheetsInternal.Delete(Name);
        }

        public IXLNamedRanges NamedRanges { get; private set; }

        public IXLNamedRange NamedRange(String rangeName)
        {
            return NamedRanges.NamedRange(rangeName);
        }

        IXLSheetView IXLWorksheet.SheetView { get => SheetView; }

        public XLSheetView SheetView { get; private set; }

        IXLTables IXLWorksheet.Tables => Tables;

        internal XLTables Tables { get; }

        public IXLTable Table(Int32 index)
        {
            return Tables.Table(index);
        }

        public IXLTable Table(String name)
        {
            return Tables.Table(name);
        }

        public IXLWorksheet CopyTo(String newSheetName)
        {
            return CopyTo(Workbook, newSheetName, Workbook.WorksheetsInternal.Count + 1);
        }

        public IXLWorksheet CopyTo(String newSheetName, Int32 position)
        {
            return CopyTo(Workbook, newSheetName, position);
        }

        public IXLWorksheet CopyTo(XLWorkbook workbook)
        {
            return CopyTo(workbook, Name, workbook.WorksheetsInternal.Count + 1);
        }

        public IXLWorksheet CopyTo(XLWorkbook workbook, String newSheetName)
        {
            return CopyTo(workbook, newSheetName, workbook.WorksheetsInternal.Count + 1);
        }

        public IXLWorksheet CopyTo(XLWorkbook workbook, String newSheetName, Int32 position)
        {
            if (this.IsDeleted)
                throw new InvalidOperationException($"`{this.Name}` has been deleted and cannot be copied.");

            var targetSheet = (XLWorksheet)workbook.WorksheetsInternal.Add(newSheetName, position);
            Internals.ColumnsCollection.ForEach(kp => kp.Value.CopyTo(targetSheet.Column(kp.Key)));
            Internals.RowsCollection.ForEach(kp => kp.Value.CopyTo(targetSheet.Row(kp.Key)));
            Internals.CellsCollection.GetCells().ForEach(c => targetSheet.Cell(c.Address).CopyFrom(c, XLCellCopyOptions.Values | XLCellCopyOptions.Styles));
            DataValidations.ForEach(dv => targetSheet.DataValidations.Add(new XLDataValidation(dv, this)));
            targetSheet.Visibility = Visibility;
            targetSheet.ColumnWidth = ColumnWidth;
            targetSheet.ColumnWidthChanged = ColumnWidthChanged;
            targetSheet.RowHeight = RowHeight;
            targetSheet.RowHeightChanged = RowHeightChanged;
            targetSheet.InnerStyle = InnerStyle;
            targetSheet.PageSetup = new XLPageSetup((XLPageSetup)PageSetup, targetSheet);
            (targetSheet.PageSetup.Header as XLHeaderFooter).Changed = true;
            (targetSheet.PageSetup.Footer as XLHeaderFooter).Changed = true;
            targetSheet.Outline = new XLOutline(Outline);
            targetSheet.SheetView = new XLSheetView(targetSheet, SheetView);
            targetSheet.SelectedRanges.RemoveAll();

            Pictures.ForEach(picture => picture.CopyTo(targetSheet));
            NamedRanges.ForEach(nr => nr.CopyTo(targetSheet));
            Tables.Cast<XLTable>().ForEach(t => t.CopyTo(targetSheet, false));
            PivotTables.ForEach<XLPivotTable>(pt => pt.CopyTo(targetSheet.Cell(pt.TargetCell.Address.CastTo<XLAddress>().WithoutWorksheet())));
            ConditionalFormats.ForEach(cf => cf.CopyTo(targetSheet));
            SparklineGroups.CopyTo(targetSheet);
            MergedRanges.ForEach(mr => targetSheet.Range(((XLRangeAddress)mr.RangeAddress).WithoutWorksheet()).Merge());
            SelectedRanges.ForEach(sr => targetSheet.SelectedRanges.Add(targetSheet.Range(((XLRangeAddress)sr.RangeAddress).WithoutWorksheet())));

            if (AutoFilter.IsEnabled)
            {
                var range = targetSheet.Range(((XLRangeAddress)AutoFilter.Range.RangeAddress).WithoutWorksheet());
                range.SetAutoFilter();
            }

            return targetSheet;
        }

        public new IXLHyperlinks Hyperlinks { get; private set; }

        IXLDataValidations IXLWorksheet.DataValidations
        {
            get { return DataValidations; }
        }

        private XLWorksheetVisibility _visibility;

        public XLWorksheetVisibility Visibility
        {
            get { return _visibility; }
            set
            {
                if (value != XLWorksheetVisibility.Visible)
                    TabSelected = false;

                _visibility = value;
            }
        }

        public IXLWorksheet Hide()
        {
            Visibility = XLWorksheetVisibility.Hidden;
            return this;
        }

        public IXLWorksheet Unhide()
        {
            Visibility = XLWorksheetVisibility.Visible;
            return this;
        }

        IXLSheetProtection IXLProtectable<IXLSheetProtection, XLSheetProtectionElements>.Protection
        {
            get => Protection;
            set => Protection = value as XLSheetProtection;
        }

        public IXLSheetProtection Protect(Algorithm algorithm = DefaultProtectionAlgorithm)
        {
            return Protection.Protect(algorithm);
        }

        public IXLSheetProtection Protect(XLSheetProtectionElements allowedElements)
            => Protection.Protect(allowedElements);

        public IXLSheetProtection Protect(Algorithm algorithm, XLSheetProtectionElements allowedElements)
            => Protection.Protect(algorithm, allowedElements);

        public IXLSheetProtection Protect(String password, Algorithm algorithm = DefaultProtectionAlgorithm)
        {
            return Protection.Protect(password, algorithm, XLSheetProtectionElements.SelectEverything);
        }

        public IXLSheetProtection Protect(String password, Algorithm algorithm, XLSheetProtectionElements allowedElements)
        {
            return Protection.Protect(password, algorithm, allowedElements);
        }

        IXLElementProtection IXLProtectable.Protect(Algorithm algorithm)
        {
            return Protect(algorithm);
        }

        IXLElementProtection IXLProtectable.Protect(String password, Algorithm algorithm)
        {
            return Protect(password, algorithm);
        }

        public IXLSheetProtection Unprotect()
        {
            return Protection.Unprotect();
        }

        public IXLSheetProtection Unprotect(String password)
        {
            return Protection.Unprotect(password);
        }

        IXLElementProtection IXLProtectable.Unprotect()
        {
            return Unprotect();
        }

        IXLElementProtection IXLProtectable.Unprotect(String password)
        {
            return Unprotect(password);
        }

        public new IXLRange Sort()
        {
            return GetRangeForSort().Sort();
        }

        public new IXLRange Sort(String columnsToSortBy, XLSortOrder sortOrder = XLSortOrder.Ascending,
                                 Boolean matchCase = false, Boolean ignoreBlanks = true)
        {
            return GetRangeForSort().Sort(columnsToSortBy, sortOrder, matchCase, ignoreBlanks);
        }

        public new IXLRange Sort(Int32 columnToSortBy, XLSortOrder sortOrder = XLSortOrder.Ascending,
                                 Boolean matchCase = false, Boolean ignoreBlanks = true)
        {
            return GetRangeForSort().Sort(columnToSortBy, sortOrder, matchCase, ignoreBlanks);
        }

        public new IXLRange SortLeftToRight(XLSortOrder sortOrder = XLSortOrder.Ascending, Boolean matchCase = false,
                                            Boolean ignoreBlanks = true)
        {
            return GetRangeForSort().SortLeftToRight(sortOrder, matchCase, ignoreBlanks);
        }

        public Boolean ShowFormulas { get; set; }

        public Boolean ShowGridLines { get; set; }

        public Boolean ShowOutlineSymbols { get; set; }

        public Boolean ShowRowColHeaders { get; set; }

        public Boolean ShowRuler { get; set; }

        public Boolean ShowWhiteSpace { get; set; }

        public Boolean ShowZeros { get; set; }

        public IXLWorksheet SetShowFormulas()
        {
            ShowFormulas = true;
            return this;
        }

        public IXLWorksheet SetShowFormulas(Boolean value)
        {
            ShowFormulas = value;
            return this;
        }

        public IXLWorksheet SetShowGridLines()
        {
            ShowGridLines = true;
            return this;
        }

        public IXLWorksheet SetShowGridLines(Boolean value)
        {
            ShowGridLines = value;
            return this;
        }

        public IXLWorksheet SetShowOutlineSymbols()
        {
            ShowOutlineSymbols = true;
            return this;
        }

        public IXLWorksheet SetShowOutlineSymbols(Boolean value)
        {
            ShowOutlineSymbols = value;
            return this;
        }

        public IXLWorksheet SetShowRowColHeaders()
        {
            ShowRowColHeaders = true;
            return this;
        }

        public IXLWorksheet SetShowRowColHeaders(Boolean value)
        {
            ShowRowColHeaders = value;
            return this;
        }

        public IXLWorksheet SetShowRuler()
        {
            ShowRuler = true;
            return this;
        }

        public IXLWorksheet SetShowRuler(Boolean value)
        {
            ShowRuler = value;
            return this;
        }

        public IXLWorksheet SetShowWhiteSpace()
        {
            ShowWhiteSpace = true;
            return this;
        }

        public IXLWorksheet SetShowWhiteSpace(Boolean value)
        {
            ShowWhiteSpace = value;
            return this;
        }

        public IXLWorksheet SetShowZeros()
        {
            ShowZeros = true;
            return this;
        }

        public IXLWorksheet SetShowZeros(Boolean value)
        {
            ShowZeros = value;
            return this;
        }

        public XLColor TabColor { get; set; }

        public IXLWorksheet SetTabColor(XLColor color)
        {
            TabColor = color;
            return this;
        }

        public Boolean TabSelected { get; set; }

        public Boolean TabActive
        {
            get { return _tabActive; }
            set
            {
                if (value && !_tabActive)
                {
                    foreach (XLWorksheet ws in Worksheet.Workbook.WorksheetsInternal)
                        ws._tabActive = false;
                }
                _tabActive = value;
            }
        }

        public IXLWorksheet SetTabSelected()
        {
            TabSelected = true;
            return this;
        }

        public IXLWorksheet SetTabSelected(Boolean value)
        {
            TabSelected = value;
            return this;
        }

        public IXLWorksheet SetTabActive()
        {
            TabActive = true;
            return this;
        }

        public IXLWorksheet SetTabActive(Boolean value)
        {
            TabActive = value;
            return this;
        }

        IXLPivotTable IXLWorksheet.PivotTable(String name)
        {
            return PivotTable(name);
        }

        IXLPivotTables IXLWorksheet.PivotTables => PivotTables;

        public XLPivotTables PivotTables { get; }

        public Boolean RightToLeft { get; set; }

        public IXLWorksheet SetRightToLeft()
        {
            RightToLeft = true;
            return this;
        }

        public IXLWorksheet SetRightToLeft(Boolean value)
        {
            RightToLeft = value;
            return this;
        }

        public override XLRanges Ranges(String ranges)
        {
            var retVal = new XLRanges();
            foreach (string rangeAddressStr in ranges.Split(',').Select(s => s.Trim()))
            {
                if (rangeAddressStr.StartsWith("#REF!"))
                {
                    continue;
                }
                else if (XLHelper.IsValidRangeAddress(rangeAddressStr))
                {
                    retVal.Add(Range(new XLRangeAddress(Worksheet, rangeAddressStr)));
                }
                else if (NamedRanges.TryGetValue(rangeAddressStr, out IXLNamedRange worksheetNamedRange))
                {
                    worksheetNamedRange.Ranges.ForEach(retVal.Add);
                }
                else if (Workbook.NamedRanges.TryGetValue(rangeAddressStr, out IXLNamedRange workbookNamedRange)
                    && workbookNamedRange.Ranges.First().Worksheet == this)
                {
                    workbookNamedRange.Ranges.ForEach(retVal.Add);
                }
            }
            return retVal;
        }

        IXLAutoFilter IXLWorksheet.AutoFilter
        {
            get { return AutoFilter; }
        }

        public IXLRows RowsUsed(XLCellsUsedOptions options = XLCellsUsedOptions.AllContents, Func<IXLRow, Boolean> predicate = null)
        {
            var rows = new XLRows(worksheet: null, StyleValue);
            var rowsUsed = new HashSet<Int32>();
            foreach (var rowNum in Internals.RowsCollection.Keys.Concat(Internals.CellsCollection.RowsUsedKeys))
            {
                if (!rowsUsed.Add(rowNum))
                {
                    continue;
                }
                var row = Row(rowNum);
                if (!row.IsEmpty(options) && (predicate == null || predicate(row)))
                    rows.Add(row);
            }
            return rows;
        }

        public IXLRows RowsUsed(Func<IXLRow, Boolean> predicate = null)
        {
            return RowsUsed(XLCellsUsedOptions.AllContents, predicate);
        }

        public IXLColumns ColumnsUsed(XLCellsUsedOptions options = XLCellsUsedOptions.AllContents, Func<IXLColumn, Boolean> predicate = null)
        {
            var columns = new XLColumns(worksheet: null, StyleValue);
            var columnsUsed = new HashSet<Int32>();
            Internals.ColumnsCollection.Keys.ForEach(r => columnsUsed.Add(r));
            Internals.CellsCollection.ColumnsUsedKeys.ForEach(r => columnsUsed.Add(r));
            foreach (var columnNum in columnsUsed)
            {
                var column = Column(columnNum);
                if (!column.IsEmpty(options) && (predicate == null || predicate(column)))
                    columns.Add(column);
            }
            return columns;
        }

        public IXLColumns ColumnsUsed(Func<IXLColumn, Boolean> predicate = null)
        {
            return ColumnsUsed(XLCellsUsedOptions.AllContents, predicate);
        }

        internal void RegisterRangeIndex(IXLRangeIndex rangeIndex)
        {
            _rangeIndices.Add(rangeIndex);
        }

        internal void Cleanup()
        {
            Internals.Dispose();
            Pictures.ForEach(p => p.Dispose());
            _rangeRepository.Clear();
            _rangeIndices.Clear();
        }

        #endregion IXLWorksheet Members

        #region Outlines

        public void IncrementColumnOutline(Int32 level)
        {
            if (level <= 0) return;
            if (_columnOutlineCount.TryGetValue(level, out Int32 value))
                _columnOutlineCount[level] = value + 1;
            else
                _columnOutlineCount.Add(level, 1);
        }

        public void DecrementColumnOutline(Int32 level)
        {
            if (level <= 0) return;
            if (_columnOutlineCount.TryGetValue(level, out Int32 value))
            {
                if (value > 0)
                    _columnOutlineCount[level] = value - 1;
            }
            else
                _columnOutlineCount.Add(level, 0);
        }

        public Int32 GetMaxColumnOutline()
        {
            var list = _columnOutlineCount.Where(kp => kp.Value > 0).ToList();
            return list.Count == 0 ? 0 : list.Max(kp => kp.Key);
        }

        public void IncrementRowOutline(Int32 level)
        {
            if (level <= 0) return;
            if (_rowOutlineCount.TryGetValue(level, out Int32 value))
                _rowOutlineCount[level] = value + 1;
            else
                _rowOutlineCount.Add(level, 0);
        }

        public void DecrementRowOutline(Int32 level)
        {
            if (level <= 0) return;
            if (_rowOutlineCount.TryGetValue(level, out Int32 value))
            {
                if (value > 0)
                    _rowOutlineCount[level] = level - 1;
            }
            else
                _rowOutlineCount.Add(level, 0);
        }

        public Int32 GetMaxRowOutline()
        {
            return _rowOutlineCount.Count == 0 ? 0 : _rowOutlineCount.Where(kp => kp.Value > 0).Max(kp => kp.Key);
        }

        #endregion Outlines

        public XLRow FirstRowUsed()
        {
            return FirstRowUsed(XLCellsUsedOptions.AllContents);
        }

        public XLRow FirstRowUsed(XLCellsUsedOptions options)
        {
            var rngRow = AsRange().FirstRowUsed(options);
            return rngRow != null ? Row(rngRow.RangeAddress.FirstAddress.RowNumber) : null;
        }

        public XLRow LastRowUsed()
        {
            return LastRowUsed(XLCellsUsedOptions.AllContents);
        }

        public XLRow LastRowUsed(XLCellsUsedOptions options)
        {
            var rngRow = AsRange().LastRowUsed(options);
            return rngRow != null ? Row(rngRow.RangeAddress.LastAddress.RowNumber) : null;
        }

        public XLColumn LastColumn()
        {
            return Column(XLHelper.MaxColumnNumber);
        }

        public XLColumn FirstColumn()
        {
            return Column(1);
        }

        public XLRow FirstRow()
        {
            return Row(1);
        }

        public XLRow LastRow()
        {
            return Row(XLHelper.MaxRowNumber);
        }

        public XLColumn FirstColumnUsed()
        {
            return FirstColumnUsed(XLCellsUsedOptions.AllContents);
        }

        public XLColumn FirstColumnUsed(XLCellsUsedOptions options)
        {
            var rngColumn = AsRange().FirstColumnUsed(options);
            return rngColumn != null ? Column(rngColumn.RangeAddress.FirstAddress.ColumnNumber) : null;
        }

        public XLColumn LastColumnUsed()
        {
            return LastColumnUsed(XLCellsUsedOptions.AllContents);
        }

        public XLColumn LastColumnUsed(XLCellsUsedOptions options)
        {
            var rngColumn = AsRange().LastColumnUsed(options);
            return rngColumn != null ? Column(rngColumn.RangeAddress.LastAddress.ColumnNumber) : null;
        }

        public XLRow Row(Int32 row)
        {
            return Row(row, true);
        }

        public XLColumn Column(Int32 columnNumber)
        {
            if (columnNumber <= 0 || columnNumber > XLHelper.MaxColumnNumber)
                throw new ArgumentOutOfRangeException(nameof(columnNumber), $"Column number must be between 1 and {XLHelper.MaxColumnNumber}");

            if (Internals.ColumnsCollection.TryGetValue(columnNumber, out XLColumn column))
                return column;
            else
            {
                // This is a new column so we're going to reference all
                // cells in this column to preserve their formatting
                Internals.RowsCollection.Keys.ForEach(r => Cell(r, columnNumber).PingStyle());

                column = RangeFactory.CreateColumn(columnNumber);
                Internals.ColumnsCollection.Add(columnNumber, column);
            }

            return column;
        }

        public IXLColumn Column(String column)
        {
            return Column(XLHelper.GetColumnNumberFromLetter(column));
        }

        public override XLRange AsRange()
        {
            return Range(1, 1, XLHelper.MaxRowNumber, XLHelper.MaxColumnNumber);
        }

        internal override void WorksheetRangeShiftedColumns(XLRange range, int columnsShifted)
        {
            if (!range.IsEntireColumn())
            {
                var model = new XLRangeAddress(
                    range.RangeAddress.FirstAddress,
                    new XLAddress(range.RangeAddress.LastAddress.RowNumber, XLHelper.MaxColumnNumber, false, false));
                var rangesToSplit = Worksheet.MergedRanges
                    .GetIntersectedRanges(model)
                    .Where(r => r.RangeAddress.FirstAddress.RowNumber < range.RangeAddress.FirstAddress.RowNumber ||
                                r.RangeAddress.LastAddress.RowNumber > range.RangeAddress.LastAddress.RowNumber)
                    .ToList();
                foreach (var rangeToSplit in rangesToSplit)
                {
                    Worksheet.MergedRanges.Remove(rangeToSplit);
                }
            }

            Workbook.Worksheets.ForEach(ws => MoveNamedRangesColumns(range, columnsShifted, ws.NamedRanges));
            MoveNamedRangesColumns(range, columnsShifted, Workbook.NamedRanges);
            ShiftConditionalFormattingColumns(range, columnsShifted);
            ShiftDataValidationColumns(range, columnsShifted);
            ShiftPageBreaksColumns(range, columnsShifted);
            RemoveInvalidSparklines();
            if (columnsShifted > 0)
            {
                var area = XLSheetRange
                    .FromRangeAddress(range.RangeAddress)
                    .ExtendRight(columnsShifted - 1);
                Workbook.CalcEngine.OnInsertAreaAndShiftRight(range.Worksheet, area);
            }
            else if (columnsShifted < 0)
            {
                var area = XLSheetRange.FromRangeAddress(range.RangeAddress);
                Workbook.CalcEngine.OnDeleteAreaAndShiftLeft(range.Worksheet, area);
            }
        }

        private void ShiftPageBreaksColumns(XLRange range, int columnsShifted)
        {
            for (var i = 0; i < PageSetup.ColumnBreaks.Count; i++)
            {
                int br = PageSetup.ColumnBreaks[i];
                if (range.RangeAddress.FirstAddress.ColumnNumber <= br)
                {
                    PageSetup.ColumnBreaks[i] = br + columnsShifted;
                }
            }
        }

        private void ShiftConditionalFormattingColumns(XLRange range, int columnsShifted)
        {
            if (!ConditionalFormats.Any()) return;
            Int32 firstCol = range.RangeAddress.FirstAddress.ColumnNumber;
            if (firstCol == 1) return;

            int colNum = columnsShifted > 0 ? firstCol - 1 : firstCol;
            var model = Column(colNum).AsRange();

            foreach (var cf in ConditionalFormats.ToList())
            {
                var cfRanges = cf.Ranges.ToList();
                cf.Ranges.RemoveAll();

                foreach (var cfRange in cfRanges)
                {
                    var cfAddress = cfRange.RangeAddress;
                    IXLRange newRange;
                    if (cfRange.Intersects(model))
                    {
                        newRange = Range(cfAddress.FirstAddress.RowNumber,
                                         cfAddress.FirstAddress.ColumnNumber,
                                         cfAddress.LastAddress.RowNumber,
                                         Math.Min(XLHelper.MaxColumnNumber, cfAddress.LastAddress.ColumnNumber + columnsShifted));
                    }
                    else if (cfAddress.FirstAddress.ColumnNumber >= firstCol)
                    {
                        newRange = Range(cfAddress.FirstAddress.RowNumber,
                                         Math.Max(cfAddress.FirstAddress.ColumnNumber + columnsShifted, firstCol),
                                         cfAddress.LastAddress.RowNumber,
                                         Math.Min(XLHelper.MaxColumnNumber, cfAddress.LastAddress.ColumnNumber + columnsShifted));
                    }
                    else
                        newRange = cfRange;

                    if (newRange.RangeAddress.IsValid &&
                        newRange.RangeAddress.FirstAddress.ColumnNumber <=
                        newRange.RangeAddress.LastAddress.ColumnNumber)
                        cf.Ranges.Add(newRange);
                }

                if (!cf.Ranges.Any())
                    ConditionalFormats.Remove(f => f == cf);
            }
        }

        private void ShiftDataValidationColumns(XLRange range, int columnsShifted)
        {
            if (!DataValidations.Any()) return;
            Int32 firstCol = range.RangeAddress.FirstAddress.ColumnNumber;
            if (firstCol == 1) return;

            int colNum = columnsShifted > 0 ? firstCol - 1 : firstCol;
            var model = Column(colNum).AsRange();

            foreach (var dv in DataValidations.ToList())
            {
                var dvRanges = dv.Ranges.ToList();
                dv.ClearRanges();

                foreach (var dvRange in dvRanges)
                {
                    var dvAddress = dvRange.RangeAddress;
                    IXLRange newRange;
                    if (dvRange.Intersects(model))
                    {
                        newRange = Range(dvAddress.FirstAddress.RowNumber,
                                         dvAddress.FirstAddress.ColumnNumber,
                                         dvAddress.LastAddress.RowNumber,
                                         Math.Min(XLHelper.MaxColumnNumber, dvAddress.LastAddress.ColumnNumber + columnsShifted));
                    }
                    else if (dvAddress.FirstAddress.ColumnNumber >= firstCol)
                    {
                        newRange = Range(dvAddress.FirstAddress.RowNumber,
                                         Math.Max(dvAddress.FirstAddress.ColumnNumber + columnsShifted, firstCol),
                                         dvAddress.LastAddress.RowNumber,
                                         Math.Min(XLHelper.MaxColumnNumber, dvAddress.LastAddress.ColumnNumber + columnsShifted));
                    }
                    else
                        newRange = dvRange;

                    if (newRange.RangeAddress.IsValid &&
                        newRange.RangeAddress.FirstAddress.ColumnNumber <=
                        newRange.RangeAddress.LastAddress.ColumnNumber)
                        dv.AddRange(newRange);
                }

                if (!dv.Ranges.Any())
                    DataValidations.Delete(v => v == dv);
            }
        }

        internal override void WorksheetRangeShiftedRows(XLRange range, int rowsShifted)
        {
            if (!range.IsEntireRow())
            {
                var model = new XLRangeAddress(
                    range.RangeAddress.FirstAddress,
                    new XLAddress(XLHelper.MaxRowNumber, range.RangeAddress.LastAddress.ColumnNumber, false, false));
                var rangesToSplit = Worksheet.MergedRanges
                    .GetIntersectedRanges(model)
                    .Where(r => r.RangeAddress.FirstAddress.ColumnNumber < range.RangeAddress.FirstAddress.ColumnNumber ||
                                r.RangeAddress.LastAddress.ColumnNumber > range.RangeAddress.LastAddress.ColumnNumber)
                    .ToList();
                foreach (var rangeToSplit in rangesToSplit)
                {
                    Worksheet.MergedRanges.Remove(rangeToSplit);
                }
            }

            Workbook.Worksheets.ForEach(ws => MoveNamedRangesRows(range, rowsShifted, ws.NamedRanges));
            MoveNamedRangesRows(range, rowsShifted, Workbook.NamedRanges);
            ShiftConditionalFormattingRows(range, rowsShifted);
            ShiftDataValidationRows(range, rowsShifted);
            RemoveInvalidSparklines();
            ShiftPageBreaksRows(range, rowsShifted);
            if (rowsShifted > 0)
            {
                var area = XLSheetRange
                    .FromRangeAddress(range.RangeAddress)
                    .ExtendBelow(rowsShifted - 1);
                Workbook.CalcEngine.OnInsertAreaAndShiftDown(range.Worksheet, area);
            }
            else if (rowsShifted < 0)
            {
                var area = XLSheetRange.FromRangeAddress(range.RangeAddress);
                Workbook.CalcEngine.OnDeleteAreaAndShiftUp(range.Worksheet, area);
            }
        }

        private void ShiftPageBreaksRows(XLRange range, int rowsShifted)
        {
            for (var i = 0; i < PageSetup.RowBreaks.Count; i++)
            {
                int br = PageSetup.RowBreaks[i];
                if (range.RangeAddress.FirstAddress.RowNumber <= br)
                {
                    PageSetup.RowBreaks[i] = br + rowsShifted;
                }
            }
        }

        private void ShiftConditionalFormattingRows(XLRange range, int rowsShifted)
        {
            if (!ConditionalFormats.Any()) return;
            Int32 firstRow = range.RangeAddress.FirstAddress.RowNumber;
            if (firstRow == 1) return;

            int rowNum = rowsShifted > 0 ? firstRow - 1 : firstRow;
            var model = Row(rowNum).AsRange();

            foreach (var cf in ConditionalFormats.ToList())
            {
                var cfRanges = cf.Ranges.ToList();
                cf.Ranges.RemoveAll();

                foreach (var cfRange in cfRanges)
                {
                    var cfAddress = cfRange.RangeAddress;
                    IXLRange newRange;
                    if (cfRange.Intersects(model))
                    {
                        newRange = Range(cfAddress.FirstAddress.RowNumber,
                                         cfAddress.FirstAddress.ColumnNumber,
                                         Math.Min(XLHelper.MaxRowNumber, cfAddress.LastAddress.RowNumber + rowsShifted),
                                         cfAddress.LastAddress.ColumnNumber);
                    }
                    else if (cfAddress.FirstAddress.RowNumber >= firstRow)
                    {
                        newRange = Range(Math.Max(cfAddress.FirstAddress.RowNumber + rowsShifted, firstRow),
                                         cfAddress.FirstAddress.ColumnNumber,
                                         Math.Min(XLHelper.MaxRowNumber, cfAddress.LastAddress.RowNumber + rowsShifted),
                                         cfAddress.LastAddress.ColumnNumber);
                    }
                    else
                        newRange = cfRange;

                    if (newRange.RangeAddress.IsValid &&
                        newRange.RangeAddress.FirstAddress.RowNumber <= newRange.RangeAddress.LastAddress.RowNumber)
                        cf.Ranges.Add(newRange);
                }

                if (!cf.Ranges.Any())
                    ConditionalFormats.Remove(f => f == cf);
            }
        }

        private void ShiftDataValidationRows(XLRange range, int rowsShifted)
        {
            if (!DataValidations.Any()) return;
            Int32 firstRow = range.RangeAddress.FirstAddress.RowNumber;
            if (firstRow == 1) return;

            int rowNum = rowsShifted > 0 ? firstRow - 1 : firstRow;
            var model = Row(rowNum).AsRange();

            foreach (var dv in DataValidations.ToList())
            {
                var dvRanges = dv.Ranges.ToList();
                dv.ClearRanges();

                foreach (var dvRange in dvRanges)
                {
                    var dvAddress = dvRange.RangeAddress;
                    IXLRange newRange;
                    if (dvRange.Intersects(model))
                    {
                        newRange = Range(dvAddress.FirstAddress.RowNumber,
                                         dvAddress.FirstAddress.ColumnNumber,
                                         Math.Min(XLHelper.MaxRowNumber, dvAddress.LastAddress.RowNumber + rowsShifted),
                                         dvAddress.LastAddress.ColumnNumber);
                    }
                    else if (dvAddress.FirstAddress.RowNumber >= firstRow)
                    {
                        newRange = Range(Math.Max(dvAddress.FirstAddress.RowNumber + rowsShifted, firstRow),
                                         dvAddress.FirstAddress.ColumnNumber,
                                         Math.Min(XLHelper.MaxRowNumber, dvAddress.LastAddress.RowNumber + rowsShifted),
                                         dvAddress.LastAddress.ColumnNumber);
                    }
                    else
                        newRange = dvRange;

                    if (newRange.RangeAddress.IsValid &&
                        newRange.RangeAddress.FirstAddress.RowNumber <= newRange.RangeAddress.LastAddress.RowNumber)
                        dv.AddRange(newRange);
                }

                if (!dv.Ranges.Any())
                    DataValidations.Delete(v => v == dv);
            }
        }

        private void RemoveInvalidSparklines()
        {
            var invalidSparklines = SparklineGroups.SelectMany(g => g)
                .Where(sl => !((XLAddress)sl.Location.Address).IsValid)
                .ToList();

            foreach (var sparkline in invalidSparklines)
            {
                Worksheet.SparklineGroups.Remove(sparkline.Location);
            }
        }

        private void MoveNamedRangesRows(XLRange range, int rowsShifted, IXLNamedRanges namedRanges)
        {
            foreach (XLNamedRange nr in namedRanges)
            {
                var newRangeList =
                    nr.RangeList.Select(r => XLCell.ShiftFormulaRows(r, this, range, rowsShifted)).Where(
                        newReference => newReference.Length > 0).ToList();
                nr.RangeList = newRangeList;
            }
        }

        private void MoveNamedRangesColumns(XLRange range, int columnsShifted, IXLNamedRanges namedRanges)
        {
            foreach (XLNamedRange nr in namedRanges)
            {
                var newRangeList =
                    nr.RangeList.Select(r => XLCell.ShiftFormulaColumns(r, this, range, columnsShifted)).Where(
                        newReference => newReference.Length > 0).ToList();
                nr.RangeList = newRangeList;
            }
        }

        public void NotifyRangeShiftedRows(XLRange range, Int32 rowsShifted)
        {
            var rangesToShift = _rangeRepository
                .Where(r => r.RangeAddress.IsValid)
                .OrderBy(r => r.RangeAddress.FirstAddress.RowNumber * -Math.Sign(rowsShifted))
                .ToList();

            WorksheetRangeShiftedRows(range, rowsShifted);

            foreach (var storedRange in rangesToShift)
            {
                if (storedRange.IsEntireColumn())
                    continue;

                if (ReferenceEquals(range, storedRange))
                    continue;

                storedRange.WorksheetRangeShiftedRows(range, rowsShifted);
            }
            range.WorksheetRangeShiftedRows(range, rowsShifted);
        }

        public void NotifyRangeShiftedColumns(XLRange range, Int32 columnsShifted)
        {
            var rangesToShift = _rangeRepository
                .Where(r => r.RangeAddress.IsValid)
                .OrderBy(r => r.RangeAddress.FirstAddress.ColumnNumber * -Math.Sign(columnsShifted))
                .ToList();

            WorksheetRangeShiftedColumns(range, columnsShifted);

            foreach (var storedRange in rangesToShift)
            {
                if (storedRange.IsEntireRow())
                    continue;

                if (ReferenceEquals(range, storedRange))
                    continue;

                storedRange.WorksheetRangeShiftedColumns(range, columnsShifted);
            }
            range.WorksheetRangeShiftedColumns(range, columnsShifted);
        }

        public XLRow Row(Int32 rowNumber, Boolean pingCells)
        {
            if (rowNumber <= 0 || rowNumber > XLHelper.MaxRowNumber)
                throw new ArgumentOutOfRangeException(nameof(rowNumber), $"Row number must be between 1 and {XLHelper.MaxRowNumber}");

            if (Internals.RowsCollection.TryGetValue(rowNumber, out XLRow row))
                return row;
            else
            {
                if (pingCells)
                {
                    // This is a new row so we're going to reference all
                    // cells in columns of this row to preserve their formatting
                    Internals.ColumnsCollection.Keys.ForEach(c => Cell(rowNumber, c).PingStyle());
                }

                row = RangeFactory.CreateRow(rowNumber);
                Internals.RowsCollection.Add(rowNumber, row);
            }

            return row;
        }

        public IXLTable Table(XLRange range, Boolean addToTables, Boolean setAutofilter = true)
        {
            return Table(range, TableNameGenerator.GetNewTableName(Workbook), addToTables, setAutofilter);
        }

        public IXLTable Table(XLRange range, String name, Boolean addToTables, Boolean setAutofilter = true)
        {
            CheckRangeNotOverlappingOtherEntities(range);
            XLRangeAddress rangeAddress;
            if (range.Rows().Count() == 1)
            {
                rangeAddress = new XLRangeAddress(range.FirstCell().Address, range.LastCell().CellBelow().Address);
                range.InsertRowsBelow(1);
            }
            else
                rangeAddress = range.RangeAddress;

            var rangeKey = new XLRangeKey(XLRangeType.Table, rangeAddress);
            var table = (XLTable)_rangeRepository.GetOrCreate(ref rangeKey);

            if (table.Name != name)
                table.Name = name;

            if (addToTables && !Tables.Contains(table))
            {
                Tables.Add(table);
            }

            if (setAutofilter && !table.ShowAutoFilter)
                table.InitializeAutoFilter();

            return table;
        }

        private void CheckRangeNotOverlappingOtherEntities(XLRange range)
        {
            // Check that the range doesn't overlap with any existing tables
            var firstOverlappingTable = Tables.FirstOrDefault<XLTable>(t => t.RangeUsed().Intersects(range));
            if (firstOverlappingTable != null)
                throw new InvalidOperationException($"The range {range.RangeAddress.ToStringRelative(includeSheet: true)} is already part of table '{firstOverlappingTable.Name}'");

            // Check that the range doesn't overlap with any filters
            if (AutoFilter.IsEnabled && this.AutoFilter.Range.Intersects(range))
                throw new InvalidOperationException($"The range {range.RangeAddress.ToStringRelative(includeSheet: true)} overlaps with the worksheet's autofilter.");
        }

        private IXLRange GetRangeForSort()
        {
            var range = RangeUsed();
            SortColumns.ForEach(e => range.SortColumns.Add(e.ElementNumber, e.SortOrder, e.IgnoreBlanks, e.MatchCase));
            SortRows.ForEach(e => range.SortRows.Add(e.ElementNumber, e.SortOrder, e.IgnoreBlanks, e.MatchCase));
            return range;
        }

        public XLPivotTable PivotTable(String name)
        {
            return (XLPivotTable)PivotTables.PivotTable(name);
        }

        public override IXLCells Cells()
        {
            return Cells(true, XLCellsUsedOptions.All);
        }

        public override XLCells Cells(Boolean usedCellsOnly)
        {
            if (usedCellsOnly)
                return Cells(true, XLCellsUsedOptions.AllContents);
            else
                return Range((this as IXLRangeBase).FirstCellUsed(XLCellsUsedOptions.All),
                             (this as IXLRangeBase).LastCellUsed(XLCellsUsedOptions.All))
                    .Cells(false, XLCellsUsedOptions.All);
        }

        public override XLCell Cell(String cellAddressInRange)
        {
            var cell = base.Cell(cellAddressInRange);
            if (cell != null) return cell;

            if (Workbook.NamedRanges.TryGetValue(cellAddressInRange, out IXLNamedRange workbookNamedRange))
            {
                if (!workbookNamedRange.Ranges.Any())
                    return null;

                return workbookNamedRange.Ranges.FirstOrDefault().FirstCell().CastTo<XLCell>();
            }

            return null;
        }

        public override XLRange Range(String rangeAddressStr)
        {
            if (XLHelper.IsValidRangeAddress(rangeAddressStr))
                return Range(new XLRangeAddress(Worksheet, rangeAddressStr));

            if (rangeAddressStr.Contains("["))
                return Table(rangeAddressStr.Substring(0, rangeAddressStr.IndexOf("["))) as XLRange;

            if (NamedRanges.TryGetValue(rangeAddressStr, out IXLNamedRange worksheetNamedRange))
                return worksheetNamedRange.Ranges.First().CastTo<XLRange>();

            if (Workbook.NamedRanges.TryGetValue(rangeAddressStr, out IXLNamedRange workbookNamedRange))
            {
                if (!workbookNamedRange.Ranges.Any())
                    return null;

                return workbookNamedRange.Ranges.First().CastTo<XLRange>();
            }

            return null;
        }

        public IXLRanges MergedRanges { get { return Internals.MergedRanges; } }

        public IXLConditionalFormats ConditionalFormats { get; private set; }

        public IXLSparklineGroups SparklineGroups => SparklineGroupsInternal;

        private IXLRanges _selectedRanges;

        public IXLRanges SelectedRanges
        {
            get
            {
                _selectedRanges?.RemoveAll(r => !r.RangeAddress.IsValid);
                return _selectedRanges;
            }
            internal set
            {
                _selectedRanges = value;
            }
        }

        public IXLCell ActiveCell { get; set; }

        private XLCalcEngine CalcEngine => Workbook.CalcEngine;

        public XLCellValue Evaluate(String expression, string formulaAddress = null)
        {
            IXLAddress address = formulaAddress is not null ? XLAddress.Create(formulaAddress) : null;
            return CalcEngine.EvaluateFormula(expression, Workbook, this, address, true).ToCellValue();
        }

        public void RecalculateAllFormulas()
        {
            Internals.CellsCollection.FormulaSlice.MarkDirty(XLSheetRange.Full);
            Workbook.CalcEngine.Recalculate(Workbook, SheetId);
        }

        public String Author { get; set; }

        public override string ToString()
        {
            return this.Name;
        }

        public IXLPictures Pictures { get; private set; }

        public Boolean IsPasswordProtected => Protection.IsPasswordProtected;

        public bool IsProtected => Protection.IsProtected;

        public IXLPicture Picture(string pictureName)
        {
            return Pictures.Picture(pictureName);
        }

        public IXLPicture AddPicture(Stream stream)
        {
            return Pictures.Add(stream);
        }

        public IXLPicture AddPicture(Stream stream, string name)
        {
            return Pictures.Add(stream, name);
        }

        internal IXLPicture AddPicture(Stream stream, string name, int Id)
        {
            return (Pictures as XLPictures).Add(stream, name, Id);
        }

        public IXLPicture AddPicture(Stream stream, XLPictureFormat format)
        {
            return Pictures.Add(stream, format);
        }

        public IXLPicture AddPicture(Stream stream, XLPictureFormat format, string name)
        {
            return Pictures.Add(stream, format, name);
        }

        public IXLPicture AddPicture(string imageFile)
        {
            return Pictures.Add(imageFile);
        }

        public IXLPicture AddPicture(string imageFile, string name)
        {
            return Pictures.Add(imageFile, name);
        }

        public override Boolean IsEntireRow()
        {
            return true;
        }

        public override Boolean IsEntireColumn()
        {
            return true;
        }

        internal IXLTable InsertTable(XLSheetPoint origin, IInsertDataReader reader, String tableName, Boolean createTable, Boolean addHeadings, Boolean transpose)
        {
            if (createTable && Tables.Any<XLTable>(t => t.Area.Contains(origin)))
                throw new InvalidOperationException($"This cell '{origin}' is already part of a table.");

            var range = InsertData(origin, reader, addHeadings, transpose);

            if (createTable)
                // Create a table and save it in the file
                return tableName == null ? range.CreateTable() : range.CreateTable(tableName);
            else
                // Create a table, but keep it in memory. Saved file will contain only "raw" data and column headers
                return tableName == null ? range.AsTable() : range.AsTable(tableName);
        }

        internal XLRange InsertData(XLSheetPoint origin, IInsertDataReader reader, Boolean addHeadings, Boolean transpose)
        {
            // Prepare data. Heading is basically just another row of data, so unify it.
            var rows = reader.GetRecords();
            var propCount = reader.GetPropertiesCount();
            if (addHeadings)
            {
                var headings = new XLCellValue[propCount];
                for (var i = 0; i < propCount; i++)
                    headings[i] = reader.GetPropertyName(i);

                rows = new[] { headings }.Concat(rows);
            }

            if (transpose)
            {
                rows = TransposeJaggedArray(rows);
            }

            var valueSlice = Internals.CellsCollection.ValueSlice;
            var styleSlice = Internals.CellsCollection.StyleSlice;

            // A buffer to avoid multiple enumerations of the source.
            var rowBuffer = new List<XLCellValue>();
            var maximumColumn = origin.Column;
            var rowNumber = origin.Row;
            foreach (var row in rows)
            {
                rowBuffer.AddRange(row);

                // InsertData should also clear data and if row doesn't have enough data,
                // fill in the rest. Only fill up to the props to be consistent. We can't
                // know how long any next row will be, so props are used as a source of truth
                // for which columns should be cleared.
                for (var i = rowBuffer.Count; i < propCount; ++i)
                    rowBuffer.Add(Blank.Value);

                // Each row can have different number of values, so we have to check every row.
                maximumColumn = Math.Max(origin.Column + rowBuffer.Count - 1, maximumColumn);
                if (maximumColumn > XLHelper.MaxColumnNumber || rowNumber > XLHelper.MaxRowNumber)
                    throw new ArgumentException("Data would write out of the sheet.");

                var column = origin.Column;
                for (var i = 0; i < rowBuffer.Count; ++i)
                {
                    var value = rowBuffer[i];
                    var point = new XLSheetPoint(rowNumber, column);
                    var modifiedStyle = GetStyleForValue(value, point);
                    if (modifiedStyle is not null)
                    {
                        if (value.IsText && value.GetText()[0] == '\'')
                            value = value.GetText().Substring(1);

                        styleSlice.Set(point, modifiedStyle);
                    }

                    valueSlice.SetCellValue(point, value);
                    column++;
                }

                rowBuffer.Clear();
                rowNumber++;
            }

            // If there is no row, rowNumber is kept at origin instead of last row + 1 .
            var lastRow = Math.Max(rowNumber - 1, origin.Row);
            var insertedArea = new XLSheetRange(origin, new XLSheetPoint(lastRow, maximumColumn));

            // If inserted area affected a table, we must fix headings and totals, because these values
            // are duplicated. Basically the table values are the truth and cells are a reflection of the
            // truth, but here we inserted shadow first.
            foreach (var table in Tables)
                table.RefreshFieldsFromCells(insertedArea);

            // Invalidate only once, not for every cell.
            Workbook.CalcEngine.MarkDirty(Worksheet, insertedArea);

            // Return area that contains all inserted cells, no matter how jagged were data.
            return Range(
                insertedArea.FirstPoint.Row,
                insertedArea.FirstPoint.Column,
                insertedArea.LastPoint.Row,
                insertedArea.LastPoint.Column);

            // Rather memory inefficient, but the original code also materialized
            // data through Linq/required multiple enumerations.
            static List<List<XLCellValue>> TransposeJaggedArray(IEnumerable<IEnumerable<XLCellValue>> enumerable)
            {
                var destination = new List<List<XLCellValue>>();

                var sourceRow = 1;
                foreach (var row in enumerable)
                {
                    var sourceColumn = 1;
                    foreach (var sourceValue in row)
                    {
                        // The original has `sourceValue` at [sourceRow, sourceColumn]
                        var destinationRowCount = destination.Count;
                        if (sourceColumn > destinationRowCount)
                            destination.Add(new List<XLCellValue>());

                        // There can be jagged arrays and the destination can have spaces between columns.
                        var destinationRow = destination[sourceColumn - 1];
                        while (destinationRow.Count < sourceRow - 1)
                            destinationRow.Add(Blank.Value);

                        destinationRow.Add(sourceValue);
                        sourceColumn++;
                    }

                    sourceRow++;
                }

                return destination;
            }
        }

        /// <summary>
        /// Get cell or null, if cell doesn't exist.
        /// </summary>
        internal XLCell GetCell(int ro, int co)
        {
            return Worksheet.Internals.CellsCollection.GetUsedCell(new XLSheetPoint(ro, co));
        }

        public XLRange GetOrCreateRange(XLRangeParameters xlRangeParameters)
        {
            var rangeKey = new XLRangeKey(XLRangeType.Range, xlRangeParameters.RangeAddress);
            var range = _rangeRepository.GetOrCreate(ref rangeKey);
            if (xlRangeParameters.DefaultStyle != null && range.StyleValue == StyleValue)
                range.InnerStyle = xlRangeParameters.DefaultStyle;

            return range as XLRange;
        }

        /// <summary>
        /// Get a range row from the shared repository or create a new one.
        /// </summary>
        /// <param name="address">Address of range row.</param>
        /// <param name="defaultStyle">Style to apply. If null the worksheet's style is applied.</param>
        /// <returns>Range row with the specified address.</returns>
        public XLRangeRow RangeRow(XLRangeAddress address, IXLStyle defaultStyle = null)
        {
            var rangeKey = new XLRangeKey(XLRangeType.RangeRow, address);
            var rangeRow = (XLRangeRow)_rangeRepository.GetOrCreate(ref rangeKey);

            if (defaultStyle != null && rangeRow.StyleValue == StyleValue)
                rangeRow.InnerStyle = defaultStyle;

            return rangeRow;
        }

        /// <summary>
        /// Get a range column from the shared repository or create a new one.
        /// </summary>
        /// <param name="address">Address of range column.</param>
        /// <param name="defaultStyle">Style to apply. If null the worksheet's style is applied.</param>
        /// <returns>Range column with the specified address.</returns>
        public XLRangeColumn RangeColumn(XLRangeAddress address, IXLStyle defaultStyle = null)
        {
            var rangeKey = new XLRangeKey(XLRangeType.RangeColumn, address);
            var rangeColumn = (XLRangeColumn)_rangeRepository.GetOrCreate(ref rangeKey);

            if (defaultStyle != null && rangeColumn.StyleValue == StyleValue)
                rangeColumn.InnerStyle = defaultStyle;

            return rangeColumn;
        }

        protected override void OnRangeAddressChanged(XLRangeAddress oldAddress, XLRangeAddress newAddress)
        {
        }

        public void RelocateRange(XLRangeType rangeType, XLRangeAddress oldAddress, XLRangeAddress newAddress)
        {
            if (_rangeRepository == null)
                return;

            var oldKey = new XLRangeKey(rangeType, oldAddress);
            var newKey = new XLRangeKey(rangeType, newAddress);
            var range = _rangeRepository.Replace(ref oldKey, ref newKey);

            foreach (var rangeIndex in _rangeIndices)
            {
                if (!rangeIndex.MatchesType(rangeType))
                    continue;

                if (rangeIndex.Remove(oldAddress) &&
                    newAddress.IsValid &&
                    range != null)
                {
                    rangeIndex.Add(range);
                }
            }
        }

        internal void DeleteColumn(int columnNumber)
        {
            Internals.ColumnsCollection.Remove(columnNumber);

            var columnsToMove = new List<Int32>(Internals.ColumnsCollection.Where(c => c.Key > columnNumber).Select(c => c.Key).OrderBy(c => c));
            foreach (int column in columnsToMove)
            {
                Internals.ColumnsCollection.Add(column - 1, Internals.ColumnsCollection[column]);
                Internals.ColumnsCollection.Remove(column);

                Internals.ColumnsCollection[column - 1].SetColumnNumber(column - 1);
            }
        }

        internal void DeleteRow(int rowNumber)
        {
            Internals.RowsCollection.Remove(rowNumber);

            var rowsToMove = new List<Int32>(Internals.RowsCollection.Where(c => c.Key > rowNumber).Select(c => c.Key).OrderBy(r => r));
            foreach (int row in rowsToMove)
            {
                Internals.RowsCollection.Add(row - 1, Worksheet.Internals.RowsCollection[row]);
                Internals.RowsCollection.Remove(row);

                Internals.RowsCollection[row - 1].SetRowNumber(row - 1);
            }
        }

        internal void DeleteRange(XLRangeAddress rangeAddress)
        {
            var rangeKey = new XLRangeKey(XLRangeType.Range, rangeAddress);
            _rangeRepository.Remove(ref rangeKey);
        }

        /// <summary>
        /// Get the actual style for a point in the sheet.
        /// </summary>
        internal XLStyleValue GetStyleValue(XLSheetPoint point)
        {
            var styleValue = Internals.CellsCollection.StyleSlice[point];
            if (styleValue is not null)
                return styleValue;

            // If the slice doesn't contain any value, determine values by inheriting.
            // Cells that lie on an intersection of a XLColumn and a XLRow have their
            // style set when column/row is created to avoid problems with correct which
            // style has precedence. I.e. set column blue, set row red => cell is red.
            // Swap order the the cell is blue.
            if (Internals.RowsCollection.TryGetValue(point.Row, out var row))
                return row.StyleValue;

            if (Internals.ColumnsCollection.TryGetValue(point.Column, out var column))
                return column.StyleValue;

            return StyleValue;
        }

        /// <summary>
        /// Get a style that should be used for a <paramref name="value"/>,
        /// if the value is set to the <paramref name="point"/>.
        /// </summary>
        internal XLStyleValue GetStyleForValue(XLCellValue value, XLSheetPoint point)
        {
            // Because StyleValue property retrieves value from a slice,
            // access it only if necessary. This happens during ever cell
            // of modification and thus is performance critical.
            switch (value.Type)
            {
                case XLDataType.DateTime:
                    {
                        var onlyDatePart = value.GetUnifiedNumber() % 1 == 0;
                        var styleValue = GetStyleValue(point);
                        if (styleValue.NumberFormat.Format.Length == 0 &&
                            styleValue.NumberFormat.NumberFormatId == 0)
                        {
                            var dateTimeNumberFormat = styleValue.NumberFormat.WithNumberFormatId(onlyDatePart ? 14 : 22);
                            return styleValue.WithNumberFormat(dateTimeNumberFormat);
                        }
                    }
                    break;

                case XLDataType.TimeSpan:
                    {
                        var styleValue = GetStyleValue(point);
                        if (styleValue.NumberFormat.Format.Length == 0 && styleValue.NumberFormat.NumberFormatId == 0)
                        {
                            var durationNumberFormat = styleValue.NumberFormat.WithNumberFormatId(46);
                            return styleValue.WithNumberFormat(durationNumberFormat);
                        }
                    }
                    break;

                case XLDataType.Text:
                    {
                        var text = value.GetText();
                        XLStyleValue styleValue = null;
                        if (text.Length > 0 && text[0] == '\'')
                        {
                            styleValue = GetStyleValue(point);
                            styleValue = styleValue.WithIncludeQuotePrefix(true);
                        }

                        var containsNewLine = text.AsSpan()
                            .Contains(Environment.NewLine.AsSpan(), StringComparison.Ordinal);
                        if (containsNewLine)
                        {
                            styleValue ??= GetStyleValue(point);
                            if (!styleValue.Alignment.WrapText)
                            {
                                styleValue = styleValue.WithAlignment(static alignment => alignment.WithWrapText(true));
                            }
                        }

                        return styleValue;
                    }
            }

            return null;
        }
    }
}
