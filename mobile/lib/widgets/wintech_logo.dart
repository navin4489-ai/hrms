import 'package:flutter/material.dart';

import '../theme/app_theme.dart';

/// In-app Wintech monogram and optional wordmark, drawn as vector artwork.
class WintechLogo extends StatelessWidget {
  const WintechLogo({
    super.key,
    this.size = 64,
    this.showWordmark = false,
    this.wordmarkColor,
  });

  final double size;
  final bool showWordmark;
  final Color? wordmarkColor;

  @override
  Widget build(BuildContext context) {
    final color = wordmarkColor ?? Theme.of(context).colorScheme.onSurface;
    final radius = BorderRadius.circular(size * 0.29);
    final mark = Container(
      width: size,
      height: size,
      decoration: BoxDecoration(
        gradient: const LinearGradient(
          begin: Alignment.topLeft,
          end: Alignment.bottomRight,
          colors: [AppColors.brandDark, AppColors.brand],
        ),
        borderRadius: radius,
        boxShadow: [
          BoxShadow(
            color: AppColors.brand.withValues(alpha: 0.24),
            blurRadius: size * 0.24,
            offset: Offset(0, size * 0.08),
          ),
        ],
      ),
      child: CustomPaint(painter: _WintechMarkPainter()),
    );

    if (!showWordmark) return mark;
    return Row(
      mainAxisSize: MainAxisSize.min,
      children: [
        mark,
        const SizedBox(width: 14),
        Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          mainAxisSize: MainAxisSize.min,
          children: [
            Text(
              'WINTECH',
              style: TextStyle(
                color: color,
                fontSize: size * 0.27,
                fontWeight: FontWeight.w800,
                letterSpacing: 1.5,
                height: 1.05,
              ),
            ),
            SizedBox(height: size * 0.06),
            Text(
              'PEOPLE & WORK',
              style: TextStyle(
                color: color.withValues(alpha: 0.72),
                fontSize: size * 0.13,
                fontWeight: FontWeight.w600,
                letterSpacing: 1.4,
              ),
            ),
          ],
        ),
      ],
    );
  }
}

class _WintechMarkPainter extends CustomPainter {
  @override
  void paint(Canvas canvas, Size size) {
    final stroke = Paint()
      ..color = Colors.white
      ..style = PaintingStyle.stroke
      ..strokeWidth = size.width * 0.085
      ..strokeCap = StrokeCap.round
      ..strokeJoin = StrokeJoin.round;
    final mark = Path()
      ..moveTo(size.width * 0.19, size.height * 0.29)
      ..lineTo(size.width * 0.35, size.height * 0.70)
      ..lineTo(size.width * 0.50, size.height * 0.43)
      ..lineTo(size.width * 0.65, size.height * 0.70)
      ..lineTo(size.width * 0.81, size.height * 0.29);
    canvas.drawPath(mark, stroke);

    final accent = Paint()..color = AppColors.accent;
    canvas.drawCircle(
      Offset(size.width * 0.77, size.height * 0.25),
      size.width * 0.055,
      accent,
    );
  }

  @override
  bool shouldRepaint(covariant _WintechMarkPainter oldDelegate) => false;
}
