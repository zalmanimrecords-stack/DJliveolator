<?php
// Command line only: served from a web root this would be an unauthenticated post-creation endpoint.
if (PHP_SAPI !== 'cli') { http_response_code(404); exit; }
require '/var/www/html/wp-load.php';
require_once ABSPATH . 'wp-admin/includes/file.php';
require_once ABSPATH . 'wp-admin/includes/media.php';
require_once ABSPATH . 'wp-admin/includes/image.php';

$content = file_get_contents( '/tmp/post.html' );
if ( ! $content ) { exit( "ERROR: no content\n" ); }

$post_id = wp_insert_post( array(
    'post_title'   => 'Liveolator 0.9: AI-Built Sets, Rock-Solid Beat Sync — and It Is All Open Source',
    'post_name'    => 'liveolator-0-9-open-source',
    'post_content' => $content,
    'post_excerpt' => 'Our free DJ + VJ app just had its biggest three months yet: an AI agent that builds and previews a whole set, a beat grid that locks to the kick, a set tempo that travels, and key detection that finally gets it right. And the whole thing is on GitHub.',
    'post_status'  => 'draft',
    'post_author'  => 1,
    'post_category' => array( 355, 8 ),
), true );

if ( is_wp_error( $post_id ) ) { exit( 'ERROR: ' . $post_id->get_error_message() . "\n" ); }

$attach_id = media_handle_sideload( array(
    'name'     => 'liveolator-dj-0-9.png',
    'tmp_name' => '/tmp/liveolator-dj.png',
), $post_id, 'Liveolator DJ view' );

if ( is_wp_error( $attach_id ) ) {
    echo 'IMAGE ERROR: ' . $attach_id->get_error_message() . "\n";
} else {
    set_post_thumbnail( $post_id, $attach_id );
    echo "thumb={$attach_id}\n";
}

echo "post={$post_id}\n";
echo 'preview=' . get_permalink( $post_id ) . "\n";
echo 'edit=https://zalmanim.com/wp-admin/post.php?post=' . $post_id . "&action=edit\n";
echo 'newsletter_meta=' . var_export( get_post_meta( $post_id, '_znl_campaign_sent', true ), true ) . "\n";
