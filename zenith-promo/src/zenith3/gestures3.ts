import type { Gesture } from "./kit";
import { GMAIL_A, GMAIL_B, GOOGLE_A, GOOGLE_B, QS } from "./Home3";
import { BACKUP_ROW_Y, HIDE_ROW_Y, HIDE_TARGET, HIDE_TOP, PLUS_ROW_CENTER, PRICE_CARD_CENTER, RESTORE_ROW_Y, sliderThumb } from "./Sheets3";
import { T3 } from "./t3";

const centre = (r: { x: number; y: number; w: number; h: number }) => [r.x + r.w / 2, r.y + r.h / 2] as const;

const [gax, gay] = centre(GMAIL_A);
const [gbx, gby] = centre(GMAIL_B);
const [oax, oay] = centre(GOOGLE_A);
const [obx, oby] = centre(GOOGLE_B);
const qsCorner = QS.x + QS.w;

const A0 = sliderThumb(0, 0, 327 / 360);
const A1 = sliderThumb(0, 0, 42 / 360);
const B0 = sliderThumb(1, 0, 262 / 360);
const B1 = sliderThumb(1, 0, 190 / 360);
const C0 = sliderThumb(2, 0, 298 / 360);
const CM = sliderThumb(2, 0, 18 / 360);
const C1 = sliderThumb(2, 0, 185 / 360);

export const GMAIL_DRAG: Gesture = {
  keys: [
    [T3.pressGmail, gax, gay],
    [T3.dragGmail, gax, gay],
    [T3.dragGmail + 0.22, 205, 330],
    [T3.dropGmail, gbx, gby],
  ],
};
export const GOOGLE_DRAG: Gesture = { keys: [[T3.dragGoogle, oax, oay], [T3.dropGoogle, obx, oby]] };

export const GESTURES3: Gesture[] = [
  { keys: [[T3.themeTap, PLUS_ROW_CENTER(0).x, PLUS_ROW_CENTER(0).y]], tap: true },
  {
    keys: [
      [T3.slideAccent - 0.02, A0.x, A0.y],
      [T3.slideAccent + 0.5, A1.x, A1.y],
      [T3.slideAccent2 - 0.02, B0.x, B0.y],
      [T3.slideAccent2 + 0.45, B1.x, B1.y],
      [T3.slideBg - 0.02, C0.x, C0.y],
      [T3.slideBgMid, CM.x, CM.y],
      [T3.slideBgMid + 0.05, CM.x, CM.y],
      [T3.slideBgEnd, C1.x, C1.y],
    ],
  },
  GMAIL_DRAG,
  GOOGLE_DRAG,
  {
    keys: [
      [T3.resizeStart + 0.12, qsCorner, QS.y + 150],
      [T3.shrinkTo, qsCorner, QS.y + 78],
      [T3.shrinkTo + 0.05, qsCorner, QS.y + 78],
      [T3.growTo, qsCorner, QS.y + 262],
    ],
  },
  { keys: [[T3.resizeDone, qsCorner, QS.y + 262]], tap: true },
  { keys: [[T3.hideToggle - 0.04, 293, HIDE_TOP + HIDE_ROW_Y(HIDE_TARGET) + 20]], tap: true },
  { keys: [[T3.backupTap, 168, BACKUP_ROW_Y]], tap: true },
  { keys: [[T3.restoreTap, 168, RESTORE_ROW_Y]], tap: true },
  { keys: [[T3.monthly - 0.04, PRICE_CARD_CENTER(0).x, PRICE_CARD_CENTER(0).y]], tap: true },
  { keys: [[T3.once - 0.04, PRICE_CARD_CENTER(1).x, PRICE_CARD_CENTER(1).y]], tap: true },
];
