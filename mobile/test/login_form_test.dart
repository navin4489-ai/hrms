import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'package:wintech_hrms/core/strings.dart';
import 'package:wintech_hrms/features/auth/login_form.dart';

/// Pumps [LoginForm] inside a scrollable host so nothing overflows on the
/// default 800x600 test surface.
Future<void> _pumpForm(
  WidgetTester tester, {
  required Future<void> Function(LoginCredentials) onSubmit,
  bool busy = false,
  String? errorText,
  VoidCallback? onForgotPassword,
}) async {
  await tester.pumpWidget(
    MaterialApp(
      home: Scaffold(
        body: SingleChildScrollView(
          child: Padding(
            padding: const EdgeInsets.all(16),
            child: LoginForm(
              onSubmit: onSubmit,
              busy: busy,
              errorText: errorText,
              onForgotPassword: onForgotPassword,
            ),
          ),
        ),
      ),
    ),
  );
}

void main() {
  testWidgets('validates both fields when submitting empty', (tester) async {
    var submitted = false;
    await _pumpForm(tester, onSubmit: (_) async => submitted = true);

    await tester.tap(find.text(Strings.signIn));
    await tester.pumpAndSettle();

    expect(find.text(Strings.fieldRequired), findsWidgets);
    expect(submitted, isFalse);
  });

  testWidgets('rejects a password shorter than 8 characters', (tester) async {
    var submitted = false;
    await _pumpForm(tester, onSubmit: (_) async => submitted = true);

    await tester.enterText(find.byType(TextFormField).first, 'employee');
    await tester.enterText(find.byType(TextFormField).last, 'short');
    await tester.tap(find.text(Strings.signIn));
    await tester.pumpAndSettle();

    expect(find.text(Strings.passwordTooShort), findsOneWidget);
    expect(submitted, isFalse);
  });

  testWidgets('submits the typed credentials when the form is valid', (
    tester,
  ) async {
    LoginCredentials? captured;
    await _pumpForm(tester, onSubmit: (creds) async => captured = creds);

    await tester.enterText(find.byType(TextFormField).first, 'employee');
    await tester.enterText(find.byType(TextFormField).last, 'Passw0rd!');
    await tester.tap(find.text(Strings.signIn));
    await tester.pumpAndSettle();

    expect(captured, isNotNull);
    expect(captured!.username, 'employee');
    expect(captured!.password, 'Passw0rd!');
  });

  testWidgets('shows administrator credential guidance instead of stale demo credentials', (tester) async {
    await _pumpForm(tester, onSubmit: (_) async {});

    expect(find.text(Strings.loginCredentialHelp), findsOneWidget);
    expect(find.textContaining('Passw0rd!'), findsNothing);
    expect(find.text('Use demo credentials'), findsNothing);
  });

  testWidgets('toggles password visibility', (tester) async {
    await _pumpForm(tester, onSubmit: (_) async {});

    // Initially obscured (eye-open icon offers to reveal it).
    expect(find.byIcon(Icons.visibility_outlined), findsOneWidget);

    await tester.tap(find.byIcon(Icons.visibility_outlined));
    await tester.pump();

    expect(find.byIcon(Icons.visibility_off_outlined), findsOneWidget);
    expect(find.byIcon(Icons.visibility_outlined), findsNothing);
  });

  testWidgets('shows the server error banner and a busy indicator while '
      'signing in', (tester) async {
    await _pumpForm(
      tester,
      onSubmit: (_) async {},
      busy: true,
      errorText: Strings.invalidCredentials,
    );

    expect(find.text(Strings.invalidCredentials), findsOneWidget);
    expect(find.text(Strings.signIn), findsNothing);
    expect(find.byType(CircularProgressIndicator), findsOneWidget);

    // Disabled inputs while busy.
    final fields = tester.widgetList<TextFormField>(find.byType(TextFormField));
    expect(fields.first.enabled, isFalse);
    expect(fields.last.enabled, isFalse);
  });

  testWidgets('invokes onForgotPassword from the link', (tester) async {
    var tapped = false;
    await _pumpForm(
      tester,
      onSubmit: (_) async {},
      onForgotPassword: () => tapped = true,
    );

    await tester.tap(find.text(Strings.forgotPassword));
    await tester.pump();
    expect(tapped, isTrue);
  });
}
