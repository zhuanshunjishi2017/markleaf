export type ScriptClass =
  | 'cjk'
  | 'latin'
  | 'digit'
  | 'space'
  | 'punctuation'
  | 'symbol'
  | 'unknown';

export type PunctuationCompression =
  | 'none'
  | 'half'
  | 'adjacent'
  | 'terminal';

export type TypographyLineAdjustment = {
  type: 'natural' | 'stretch' | 'shrink' | 'overflow';
  amount: number;
};

export type LayoutItem =
  | {
      kind: 'box';
      text: string;
    script: ScriptClass;
    from: number;
    to: number;
    width: number;
    stretch: number;
    shrink: number;
  }
  | {
      kind: 'glue';
      text: string;
      script: ScriptClass;
      from: number;
      to: number;
      width: number;
      stretch: number;
      shrink: number;
    }
  | {
      kind: 'penalty';
      from: number;
      to: number;
      penalty: number;
      flagged: boolean;
    };

export type BreakCandidate = {
  itemIndex: number;
  sourceTo: number;
  penalty: number;
  flagged: boolean;
};

export type TypographyLine = {
  from: number;
  to: number;
  contentTo: number;
  naturalWidth: number;
  adjustedWidth: number;
  adjustment: TypographyLineAdjustment;
  spaceCount: number;
  badness: number;
  breakReason: 'optimal' | 'forced' | 'paragraph-end';
};

export type TypographyFallbackReason =
  | 'no-legal-break'
  | 'badness-exceeded'
  | 'atom-wider-than-line';

export type LayoutPlan = {
  lines: TypographyLine[];
  fallback: boolean;
  fallbackReason?: TypographyFallbackReason;
};

export type TextMetrics = {
  width: number;
  stretch: number;
  shrink: number;
};
