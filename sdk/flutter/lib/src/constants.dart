const String kLevelMomentBreakUrl = 'https://levelmoment.com/break';
const String kLevelMomentAccessUrl = 'https://levelmoment.com/access';
const String kLevelMomentApiUrl = 'https://levelmoment.com/api';
const String kLevelMomentSdkVersion = '0.2.0';
const int kLevelMomentProtocolVersion = 1;

String levelMomentOrigin(String url) {
  final uri = Uri.tryParse(url);
  if (uri == null || !uri.hasScheme || !uri.hasAuthority) return '';
  final isDefaultPort = (uri.scheme == 'https' && uri.port == 443) ||
      (uri.scheme == 'http' && uri.port == 80);
  final port = uri.hasPort && !isDefaultPort ? ':${uri.port}' : '';
  return '${uri.scheme}://${uri.host}$port';
}
